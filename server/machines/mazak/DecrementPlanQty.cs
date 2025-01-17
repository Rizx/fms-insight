/* Copyright (c) 2019, John Lenz

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
using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface
{
  public interface IDecrementPlanQty
  {
    void Decrement(IRepository jobDB, DateTime? now = null);
  }

  public class DecrementPlanQty : IDecrementPlanQty
  {
    private IWriteData _write;
    private IReadDataAccess _read;

    private static Serilog.ILogger Log = Serilog.Log.ForContext<DecrementPlanQty>();

    public DecrementPlanQty(IWriteData w, IReadDataAccess r)
    {
      _write = w;
      _read = r;
    }

    public void Decrement(IRepository jobDB, DateTime? now = null)
    {
      // This works in three steps:
      //
      // - First, the planned quantity of the schedules are reduced to equal the total number of parts being machined, completed,
      //   or about to be loaded.
      // - Next, the quantites that were removed are recorded.
      // - Finally, the Queues code in Queues.cs will update the material available and remove any allocated castings on
      //   the schedule.
      //
      // We are protected against crashes and failures:
      // - If the a failure happens before the decrement quantities are recorded, the next decrement will
      //   resume and continue reducing the mazak planned quantities.  The decrement quantities are then
      //   calculated by comparing the original job plan quantity with the current mazak schedule quantity, no matter
      //   when the mazak schedule was reduced.
      //
      // - If a failure happens after the decrement quantities are recorded, the job will not be decremented again because
      //   the code checks if a previous decrement has happended.  The queues code will keep re-trying to sync the material
      //   quantities with the new scheduled quantities.

      var jobs = JobsToDecrement(jobDB, _read.LoadStatusAndTools());
      Log.Debug("Found jobs to decrement {@jobs}", jobs);

      if (jobs.Count == 0) return;

      ReducePlannedQuantity(jobs);
      RecordDecrement(jobDB, jobs, now);
    }


    private class DecrSchedule
    {
      public MazakScheduleRow Schedule { get; set; }
      public HistoricJob Job { get; set; }
      public int Proc1Path { get; set; }
      public int NewPlanQty { get; set; }
    }

    private List<DecrSchedule> JobsToDecrement(IRepository jobDB, MazakCurrentStatus schedules)
    {
      var jobs = new List<DecrSchedule>();

      foreach (var sch in schedules.Schedules)
      {
        //parse schedule
        if (!MazakPart.IsSailPart(sch.PartName, sch.Comment)) continue;
        if (string.IsNullOrEmpty(sch.Comment)) continue;
        MazakPart.ParseComment(sch.Comment, out string unique, out var procToPath, out bool manual);
        if (manual) continue;

        //load the job
        if (string.IsNullOrEmpty(unique)) continue;
        var job = jobDB.LoadJob(unique);
        if (job == null) continue;

        // if already decremented, ignore
        if (jobDB.LoadDecrementsForJob(unique).Any()) continue;


        // check load is in process
        var loadOpers = schedules.LoadActions;
        var loadingQty = 0;
        if (loadOpers != null)
        {
          foreach (var action in loadOpers)
          {
            if (action.Unique == job.UniqueStr && action.Process == 1 && action.LoadEvent && action.Path == procToPath.PathForProc(action.Process))
            {
              loadingQty += action.Qty;
              Log.Debug("Found {uniq} is in the process of being loaded action {@action}", job.UniqueStr, action);
            }
          }
        }

        jobs.Add(new DecrSchedule()
        {
          Schedule = sch,
          Job = job,
          Proc1Path = procToPath.PathForProc(proc: 1),
          NewPlanQty = CountCompletedOrMachiningStarted(sch) + loadingQty
        });
      }

      return jobs;
    }


    private void ReducePlannedQuantity(ICollection<DecrSchedule> jobs)
    {
      var schs = new List<MazakScheduleRow>();
      foreach (var job in jobs)
      {
        if (job.Schedule.PlanQuantity > job.NewPlanQty)
        {
          var newSchRow = job.Schedule with
          {
            Command = MazakWriteCommand.ScheduleSafeEdit,
            PlanQuantity = job.NewPlanQty,
            Processes = new List<MazakScheduleProcessRow>()
          };
          schs.Add(newSchRow);
        }
      }

      if (schs.Count > 0)
      {
        var write = new MazakWriteData() { Schedules = schs };
        _write.Save(write, "Decrement preventing new parts starting");
      }
    }

    private void RecordDecrement(IRepository jobDB, List<DecrSchedule> decrs, DateTime? now)
    {
      var decrsByJob = decrs.GroupBy(d => d.Job.UniqueStr);

      var decrAmt = new List<NewDecrementQuantity>();
      foreach (var decrsForJob in decrsByJob)
      {
        var job = decrsForJob.First().Job;
        var planned = job.Cycles;
        var newPlanQty = decrsForJob.Sum(d => d.NewPlanQty);
        if (planned > newPlanQty)
        {
          decrAmt.Add(new NewDecrementQuantity()
          {
            JobUnique = job.UniqueStr,
            Part = job.PartName,
            Quantity = planned - newPlanQty
          });
        }
      }

      var oldJobs = jobDB.LoadJobsNotCopiedToSystem(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow.AddHours(1), includeDecremented: false);
      foreach (var j in oldJobs)
      {
        decrAmt.Add(new NewDecrementQuantity()
        {
          JobUnique = j.UniqueStr,
          Part = j.PartName,
          Quantity = j.Cycles
        });
      }

      if (decrAmt.Count > 0)
      {
        jobDB.AddNewDecrement(decrAmt, now);
      }
    }

    public static int CountCompletedOrMachiningStarted(MazakScheduleRow sch)
    {
      var cnt = sch.CompleteQuantity;
      foreach (var schProcRow in sch.Processes)
      {
        cnt += schProcRow.ProcessBadQuantity + schProcRow.ProcessExecuteQuantity;
        if (schProcRow.ProcessNumber > 1)
          cnt += schProcRow.ProcessMaterialQuantity;
      }
      return cnt;
    }
  }
}
