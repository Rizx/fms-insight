/* Copyright (c) 2020, John Lenz

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
using System.Linq;
using System.Collections.Generic;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using Microsoft.Extensions.Configuration;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BlackMaple.MachineFramework.Tests")]

namespace BlackMaple.FMSInsight.Niigata
{

  public class NiigataBackend : IFMSBackend, IDisposable
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<NiigataBackend>();
    private NiigataICC _icc;
    private NiigataJobs _jobControl;
    private SyncPallets _sync;

    public JobLogDB LogDB { get; private set; }
    public JobDB JobDB { get; private set; }
    public ISyncPallets SyncPallets => _sync;
    public NiigataStationNames StationNames { get; }
    public ICncMachineConnection MachineConnection { get; }

    public NiigataBackend(
      IConfigurationSection config,
      FMSSettings cfg,
      bool startSyncThread,
      Func<JobLogDB, IRecordFacesForPallet, NiigataStationNames, ICncMachineConnection, IAssignPallets> customAssignment = null
    )
    {
      try
      {
        Log.Information("Starting niigata backend");
        LogDB = new JobLogDB(cfg);
        LogDB.Open(
            System.IO.Path.Combine(cfg.DataDirectory, "niigatalog.db"),
            startingSerial: cfg.StartingSerial
        );
        JobDB = new JobDB();
        JobDB.Open(
          System.IO.Path.Combine(cfg.DataDirectory, "niigatajobs.db")
        );

        var programDir = config.GetValue<string>("Program Directory");
        if (!System.IO.Directory.Exists(programDir))
        {
          Log.Error("Program directory {dir} does not exist", programDir);
        }

        var reclampNames = config.GetValue<string>("Reclamp Group Names");
        var machineNames = config.GetValue<string>("Machine Names");
        StationNames = new NiigataStationNames()
        {
          ReclampGroupNames = new HashSet<string>(
            string.IsNullOrEmpty(reclampNames) ? Enumerable.Empty<string>() : reclampNames.Split(',').Select(s => s.Trim())
          ),
          IccMachineToJobMachNames = string.IsNullOrEmpty(machineNames)
            ? new Dictionary<int, (string group, int num)>()
            : machineNames.Split(',').Select((machineName, idx) =>
            {
              var lastNumIdx = machineName.LastIndexOfAny(new[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' });
              if (lastNumIdx >= 0 && int.TryParse(machineName.Substring(lastNumIdx), out var num))
              {
                return new { iccMc = idx + 1, group = machineName.Substring(0, lastNumIdx), num = num };
              }
              else
              {
                return null;
              }
            })
            .Where(x => x != null)
            .ToDictionary(x => x.iccMc, x => (group: x.group, num: x.num))
        };
        Log.Debug("Using station names {@names}", StationNames);

        var machineIps = config.GetValue<string>("Machine IP Addresses");
        MachineConnection = new CncMachineConnection(
          string.IsNullOrEmpty(machineIps) ? Enumerable.Empty<string>() : machineIps.Split(',').Select(s => s.Trim())
        );

        var connStr = config.GetValue<string>("Connection String");

        _icc = new NiigataICC(JobDB, programDir, connStr, StationNames);
        var recordFaces = new RecordFacesForPallet(LogDB);
        var createLog = new CreateCellState(LogDB, JobDB, recordFaces, cfg, StationNames, MachineConnection);

        IAssignPallets assign;
        if (customAssignment != null)
        {
          assign = customAssignment(LogDB, recordFaces, StationNames, MachineConnection);
        }
        else
        {
          assign = new MultiPalletAssign(new IAssignPallets[] {
            new AssignNewRoutesOnPallets(recordFaces, StationNames),
            new SizedQueues(cfg.Queues)
          });
        }
        _sync = new SyncPallets(JobDB, LogDB, _icc, assign, createLog);
        _jobControl = new NiigataJobs(JobDB, LogDB, cfg, _sync, StationNames);
        if (startSyncThread)
        {
          StartSyncThread();
        }
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Unhandled exception when initializing niigata backend");
      }
    }

    public void StartSyncThread()
    {
      _sync.StartThread();
    }

    public void Dispose()
    {
      if (_jobControl != null) _jobControl.Dispose();
      _jobControl = null;
      if (SyncPallets != null) _sync.Dispose();
      _sync = null;
      if (_icc != null) _icc.Dispose();
      _icc = null;
      if (LogDB != null) LogDB.Close();
      LogDB = null;
      if (JobDB != null) JobDB.Close();
      JobDB = null;
    }

    public IInspectionControl InspectionControl()
    {
      return LogDB;
    }

    public IJobDatabase JobDatabase()
    {
      return JobDB;
    }

    public IOldJobDecrement OldJobDecrement()
    {
      return null;
    }

    public IJobControl JobControl()
    {
      return _jobControl;
    }

    public ILogDatabase LogDatabase()
    {
      return LogDB;
    }
  }

  public static class NiigataProgram
  {
    public static void Main()
    {
#if DEBUG
      var useService = false;
#else
      var useService = true;
#endif
      Program.Run(useService, (cfg, fmsSt) =>
        new FMSImplementation()
        {
          Backend = new NiigataBackend(cfg.GetSection("Niigata"), fmsSt, startSyncThread: true),
          Name = "Niigata",
          Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
        });
    }
  }
}