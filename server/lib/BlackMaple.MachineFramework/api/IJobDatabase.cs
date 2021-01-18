/* Copyright (c) 2017, John Lenz

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

namespace BlackMaple.MachineWatchInterface
{
  public interface IJobDatabase : IDisposable
  {
    JobPlan LoadJob(string uniqueStr);

    ///Load all jobs, station, and tool utilization which intersect the given date range.
    HistoricData LoadJobHistory(DateTime startUTC, DateTime endUTC);

    ///Loads all jobs which have a unique strictly larger than the given unique
    HistoricData LoadJobsAfterScheduleId(string scheduleId);

    ///Loads all jobs for the most recent schedule
    PlannedSchedule LoadMostRecentSchedule();

    List<PartWorkorder> MostRecentUnfilledWorkordersForPart(string part);
    void ReplaceWorkordersForSchedule(string scheduleId, IEnumerable<MachineWatchInterface.PartWorkorder> newWorkorders, IEnumerable<MachineWatchInterface.ProgramEntry> programs, DateTime? nowUtc = null);
  }
}