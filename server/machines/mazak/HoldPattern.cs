/* Copyright (c) 2018, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided
      with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or
      promote products derived from this software without specific
      prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using BlackMaple.MachineWatchInterface;
using Microsoft.Data.Sqlite;

namespace MazakMachineInterface
{
  public interface IHoldManagement
  {
    TimeSpan CheckForTransition(MazakSchedules schedules, BlackMaple.MachineFramework.JobDB jobDB);
  }

  public class HoldPattern : IHoldManagement
  {
    #region Public API
    //Each schedule can be in one of four states: Shift1, Shift2, Preperation, or FullHold.
    //Then the mazak cell controller has two settings which can be changed on the main screen:
    // - Machining:  "Shift1", "Shift2", or "Shift1&2"
    // - LoadUnload: "Shift1", "Shift2", or "Shift1&2"
    //These settings determine which schedules to run.  Preperation means only load events happen.
    //
    //If we want to hold machining we will put the schedule in the Preperation state.  Once the hold
    //passes, we use a thread to switch the schedule to the Shift1 state so that it starts running.
    //Note that we do not currently support holding load unload.  If we wanted, we could use the Shift2
    //to hold LoadUnload.  For example, put Machining in "Shift1&2" and LUL in Shift1.  Then
    //schedules in the Shift2 state would not be loaded.

    public enum HoldMode
    {
      Shift1 = 0,
      FullHold = 1,
      Preperation = 2,
      Shift2 = 3
    }

    public static HoldMode CalculateHoldMode(bool entireHold, bool machineHold)
    {
      if (entireHold)
        return HoldMode.FullHold;
      else if (machineHold)
        return HoldMode.Preperation;
      else
        return HoldMode.Shift1;
    }

    private static Serilog.ILogger Log = Serilog.Log.ForContext<HoldPattern>();
    private IWriteData database;

    public HoldPattern(IWriteData d)
    {
      database = d;
    }

    #endregion

    #region Mazak Schedules

    private class MazakSchedule
    {
      public int SchId
      {
        get { return _schRow.Id; }
      }

      public string UniqueStr
      {
        get
        {
          MazakPart.ParseComment(_schRow.Comment, out string unique, out var paths, out var manual);
          return unique;
        }
      }

      public HoldMode Hold
      {
        get { return (HoldMode)_schRow.HoldMode; }
      }

      public JobHoldPattern HoldEntireJob { get; }
      public JobHoldPattern HoldMachining { get; }

      public void ChangeHoldMode(HoldMode newHold)
      {
        var transSet = new MazakWriteData();
        var newSchRow = _schRow.Clone();
        newSchRow.Command = MazakWriteCommand.ScheduleSafeEdit;
        newSchRow.HoldMode = (int)newHold;
        transSet.Schedules.Add(newSchRow);

        _parent.database.Save(transSet, "Hold Mode");
      }

      public MazakSchedule(BlackMaple.MachineFramework.JobDB jdb, HoldPattern parent, MazakScheduleRow s)
      {
        _parent = parent;
        _schRow = s;

        if (MazakPart.IsSailPart(_schRow.PartName, _schRow.Comment))
        {
          MazakPart.ParseComment(_schRow.Comment, out string unique, out var paths, out var manual);
          var job = jdb.LoadJob(unique);
          if (job != null)
          {
            HoldEntireJob = job.HoldEntireJob;
            HoldMachining = job.HoldMachining(process: 1, path: paths.PathForProc(proc: 1));
          }
        }
      }

      private HoldPattern _parent;
      private MazakScheduleRow _schRow;
    }

    private IDictionary<int, MazakSchedule> LoadMazakSchedules(BlackMaple.MachineFramework.JobDB jdb, IEnumerable<MazakScheduleRow> schedules)
    {
      var ret = new Dictionary<int, MazakSchedule>();

      foreach (var schRow in schedules)
      {
        ret.Add(schRow.Id, new MazakSchedule(jdb, this, schRow));
      }

      return ret;
    }

    public TimeSpan CheckForTransition(MazakSchedules schedules, BlackMaple.MachineFramework.JobDB jobDB)
    {
      try
      {
        //Store the current time, this is the time we use to calculate the hold pattern.
        //It is important that this time be fixed for all schedule calculations, otherwise
        //we might miss a transition if it occurs while we are running this function.
        var nowUTC = DateTime.UtcNow;

        IDictionary<int, MazakSchedule> mazakSch;
        mazakSch = LoadMazakSchedules(jobDB, schedules.Schedules);

        Log.Debug("Checking for hold transitions at {time} ", nowUTC);

        var nextTimeUTC = DateTime.MaxValue;

        foreach (var pair in mazakSch)
        {
          bool allHold = false;
          DateTime allNext = DateTime.MaxValue;
          bool machHold = false;
          DateTime machNext = DateTime.MaxValue;

          if (pair.Value.HoldEntireJob != null)
            pair.Value.HoldEntireJob.HoldInformation(nowUTC, out allHold, out allNext);
          if (pair.Value.HoldMachining != null)
            pair.Value.HoldMachining.HoldInformation(nowUTC, out machHold, out machNext);

          HoldMode currentHoldMode = CalculateHoldMode(allHold, machHold);

          Log.Debug("Checking schedule {sch}, mode {mode}, target {targetMode}, next {allNext}, mach {machNext}",
            pair.Key, pair.Value.Hold, currentHoldMode, allNext, machNext
          );

          if (currentHoldMode != pair.Value.Hold)
          {
            pair.Value.ChangeHoldMode(currentHoldMode);
          }

          if (allNext < nextTimeUTC)
            nextTimeUTC = allNext;
          if (machNext < nextTimeUTC)
            nextTimeUTC = machNext;
        }

        Log.Debug("Next hold transition {next}", nextTimeUTC);

        if (nextTimeUTC == DateTime.MaxValue)
          return TimeSpan.MaxValue;
        else
          return nextTimeUTC.Subtract(DateTime.UtcNow);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Unhanlded error checking for hold transition");

        //Try again in three minutes.
        return TimeSpan.FromMinutes(3);
      }
    }

    #endregion
  }
}