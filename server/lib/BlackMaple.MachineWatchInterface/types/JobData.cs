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
using System.Linq;
using System.Runtime.Serialization;

namespace BlackMaple.MachineWatchInterface
{

    //stores information about a single "stop" of the pallet in a route
    [Serializable, DataContract]
    public class JobMachiningStop
    {
        public string StationGroup
        {
            get { return _statGroup; }
	    set { _statGroup = value; }
        }

        public TimeSpan ExpectedCycleTime
        {
            get { return _expectedCycleTime; }
            set { _expectedCycleTime = value; }
        }

        public void AddProgram(int station, string program)
        {
            _programs[station] = program;
        }

        public string Program(int statNum)
        {
            if (_programs.ContainsKey(statNum)) {
                return _programs[statNum];
            } else {
                return "";
            }
        }

        public IEnumerable<ProgramEntry> AllPrograms()
        {
            var ret = new List<ProgramEntry>();
            foreach (var k in _programs) {
                ret.Add(new ProgramEntry(k.Key, k.Value));
            }
            return ret;
        }

        public IEnumerable<int> Stations()
        {
            return _programs.Keys;
        }

        [Serializable, DataContract]
        public struct ProgramEntry : IComparable<ProgramEntry>
        {
            [DataMember] public int StationNum;
            [DataMember] public string Program;

            internal ProgramEntry(int stationNum, string program)
            {
                StationNum = stationNum;
                Program = program;
            }

            public override string ToString()
            {
                return StationNum.ToString() + ":" + Program;
            }

            public int CompareTo(ProgramEntry o)
            {
                var i = StationNum.CompareTo(o.StationNum);
                if (i < 0) return -1;
                if (i > 0) return 1;
                return Program.CompareTo(o.Program);
            }
        }

        //Key is tool name, value is expected elapsed time
        public IDictionary<string, TimeSpan> Tools
        {
            get { return _tools; }
        }

        public JobMachiningStop(string sGroup)
        {
            _statGroup = sGroup;
            _programs = new Dictionary<int, string>();
            _tools = new Dictionary<string, TimeSpan>();
            _expectedCycleTime = TimeSpan.Zero;
        }

        public JobMachiningStop(JobMachiningStop stop)
        {
            _statGroup = stop._statGroup;
            _programs = new Dictionary<int, string>(stop._programs);
            _expectedCycleTime = stop._expectedCycleTime;
            _tools = new Dictionary<string, TimeSpan>(stop._tools);
        }

        [DataMember(Name="Stations")]
        private Dictionary<int, string> _programs;

        [DataMember(Name="Tools")]
        private Dictionary<string, TimeSpan> _tools; //key is tool, value is expected cutting time

        [DataMember(Name="StationGroup")]
        private string _statGroup;

        [DataMember(Name="ExpectedCycleTime")]
        private TimeSpan _expectedCycleTime;
    }

    [Serializable, DataContract]
    public class JobInspectionData
    {
        [DataMember]
        public readonly string InspectionType;

        //There are two possible ways of triggering an exception: counts and frequencies.
        // * For counts, the MaxVal will contain a number larger than zero and RandomFreq will contain -1
        // * For frequencies, the value of MaxVal is -1 and RandomFreq contains
        //   the frequency as a number between 0 and 1.

        //Every time a material completes, the counter string is expanded (see below).
        [DataMember]
        public readonly string Counter;

        //For each completed material, the counter is incremented.  If the counter is equal to MaxVal,
        //we signal an inspection and reset the counter to 0.
        [DataMember]
        public readonly int MaxVal;

        //The random frequency of inspection
        [DataMember]
        public readonly double RandomFreq;

        //If the last inspection signaled for this counter was longer than TimeInterval,
        //signal an inspection.  This can be disabled by using TimeSpan.Zero
        [DataMember]
        public readonly TimeSpan TimeInterval;

        //If set to -1, the entire job should be inspected once the job completes.
        //If set to a positive number, only that process should have the inspection triggered.
        [DataMember]
        public readonly int InspectSingleProcess;

        public JobInspectionData(string iType, string ctr, int max, TimeSpan interval, int inspSingleProc = -1)
        {
            InspectionType = iType;
            Counter = ctr;
            MaxVal = max;
            TimeInterval = interval;
            RandomFreq = -1;
            InspectSingleProcess = inspSingleProc;
        }
        public JobInspectionData(string iType, string ctr, double frequency, TimeSpan interval, int inspSingleProc = -1)
        {
            InspectionType = iType;
            Counter = ctr;
            MaxVal = -1;
            TimeInterval = interval;
            RandomFreq = frequency;
            InspectSingleProcess = inspSingleProc;
        }
        public JobInspectionData(JobInspectionData insp)
        {
            InspectionType = insp.InspectionType;
            Counter = insp.Counter;
            MaxVal = insp.MaxVal;
            TimeInterval = insp.TimeInterval;
            RandomFreq = insp.RandomFreq;
            InspectSingleProcess = insp.InspectSingleProcess;
        }

        //The final counter string is determined by replacing following substrings in the counter
        public static string PalletFormatFlag(int proc)
        {
            return "%pal" + proc.ToString() + "%";
        }
        public static string LoadFormatFlag(int proc)
        {
            return "%load" + proc.ToString() + "%";
        }
        public static string UnloadFormatFlag(int proc)
        {
            return "%unload" + proc.ToString() + "%";
        }
        public static string StationFormatFlag(int proc, int routeNum)
        {
            return "%stat" + proc.ToString() + "," + routeNum.ToString() + "%";
        }
    }

    [Serializable, DataContract]
    public class JobHoldPattern
    {
        // All of the following hold types are an OR, meaning if any one of them says a hold is in effect,
        // the job is on hold.

        [DataMember]
        public bool UserHold;

        [DataMember]
        public string ReasonForUserHold;

        //A list of timespans the job should be on hold/not on hold.
        //During the first timespan, the job is on hold.
        [DataMember]
        public readonly IList<TimeSpan> HoldUnholdPattern;

        [DataMember]
        public DateTime HoldUnholdPatternStartUTC;

        [DataMember]
        public bool HoldUnholdPatternRepeats;

        public bool IsJobOnHold
        {
            get
            {
                bool hold;
                DateTime next;

                HoldInformation(DateTime.UtcNow, out hold, out next);

                return hold;
            }
        }

        // Given a time, allows you to calculate if the hold is active
        // and the next transition time.
        public void HoldInformation(DateTime nowUTC,
                                    out bool isOnHold,
                                    out DateTime nextTransitionUTC)
        {

            if (UserHold) {
                isOnHold = true;
                nextTransitionUTC = DateTime.MaxValue;
                return;
            }

            if (HoldUnholdPattern.Count == 0) {
                isOnHold = false;
                nextTransitionUTC = DateTime.MaxValue;
                return;
            }

            //check that the hold pattern has a non-zero timespan.
            //Without this check we will get in an infinite loop below.
            bool foundSpan = false;
            foreach (var span in HoldUnholdPattern) {
                if (span.Ticks > 0) {
                    foundSpan = true;
                    break;
                }
            }
            if (!foundSpan) {
                //If not, then we are not on hold.
                isOnHold = false;
                nextTransitionUTC = DateTime.MaxValue;
                return;
            }

            if (nowUTC < HoldUnholdPatternStartUTC) {
                isOnHold = false;
                nextTransitionUTC = HoldUnholdPatternStartUTC;
            }

            //Start with a span from the current time to the start time.
            //We will remove time from this span until it goes negative, indicating
            //that we have found the point in the pattern containing the current time.
            var remainingSpan = nowUTC.Subtract(HoldUnholdPatternStartUTC);

            int curIndex = 0;

            do {

                // Decrement the time.
                remainingSpan = remainingSpan.Subtract(HoldUnholdPattern[curIndex]);

                // If we pass 0, we have found the span with the current time.
                if (remainingSpan.Ticks < 0) {
                    //since remainingSpan is negative, we subtract to add the time to the next transition.
                    isOnHold = (curIndex % 2 == 0);
                    nextTransitionUTC = nowUTC.Subtract(remainingSpan);
                    return;
                }

                curIndex += 1;

                // check for repeat patterns
                if (curIndex >= HoldUnholdPattern.Count && HoldUnholdPatternRepeats)
                    curIndex = 0;

            } while (curIndex < HoldUnholdPattern.Count && remainingSpan.Ticks > 0);

            //We are past the end of the pattern, so we are not on hold.
            isOnHold = false;
            nextTransitionUTC = DateTime.MaxValue;
        }

        public JobHoldPattern()
        {
            UserHold = false;
            ReasonForUserHold = "";

            HoldUnholdPattern = new List<TimeSpan>();
            HoldUnholdPatternStartUTC = new DateTime(2000, 1, 1);
            HoldUnholdPatternRepeats = false;
        }

        public JobHoldPattern(JobHoldPattern pattern)
        {
            UserHold = pattern.UserHold;
            ReasonForUserHold = pattern.ReasonForUserHold;
            HoldUnholdPattern = new List<TimeSpan>(pattern.HoldUnholdPattern);
            HoldUnholdPatternStartUTC = pattern.HoldUnholdPatternStartUTC;
            HoldUnholdPatternRepeats = pattern.HoldUnholdPatternRepeats;
        }
    }

    [Serializable, DataContract]
    public class JobPlan
    {
        public string UniqueStr
        {
            get { return _uniqueStr; }
        }

        //general info about the route

        //The overall starting time of the period when we expect the job to run.
        //Note that the job might not immediately start at this point, the expected
        //start time from the simulation is passed per path in the SimulatedStartingTime
        public DateTime RouteStartingTimeUTC
        {
            get { return _routeStartUTC; }
            set { _routeStartUTC = value; }
        }
        public DateTime RouteEndingTimeUTC
        {
            get { return _routeEndUTC; }
            set { _routeEndUTC = value; }
        }
        public bool Archived
        {
            get { return _archived; }
            set { _archived = value; }
        }
        public string ScheduleId
        {
            get { return _scheduleId; }
            set { _scheduleId = value; }
        }
        public bool JobCopiedToSystem
        {
            get { return _copiedToSystem; }
            set { _copiedToSystem = value; }
        }
        public int Priority
        { // larger number means higher priority
            get { return _priority; }
            set { _priority = value; }
        }
        public string PartName
        {
            get { return _partName; }
            set { _partName = value; }
        }
        public string Comment
        {
            get { return _comment; }
            set { _comment = value; }
        }
        public int NumProcesses
        {
            get { return _procPath.Length; }
        }
        public bool ManuallyCreatedJob
        {
            get { return _manuallyCreated; }
            set { _manuallyCreated = value; }
        }
        public bool CreateMarkerData
        {
            get { return _createMarker; }
            set { _createMarker = value; }
        }
        public ICollection<string> ScheduledBookingIds
        {
            get { return _scheduledIds; }
        }
        public int GetNumPaths(int process)
        {
            if (process >= 1 && process <= NumProcesses) {
                return _procPath[process - 1].Length;
            } else {
                throw new IndexOutOfRangeException("Invalid process number");
            }
        }
        public int GetPathGroup(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].PathGroup;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void SetPathGroup(int process, int path, int pgroup)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].PathGroup = pgroup;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }


        // Hold Status
        public JobHoldPattern HoldEntireJob
        {
            get { return _holdJob; }
        }
        public JobHoldPattern HoldMachining(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].HoldMachining;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public JobHoldPattern HoldLoadUnload(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].HoldLoadUnload;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }

        // Planned cycles
        public int GetPlannedCyclesOnFirstProcess(int path)
        {
            if (path >= 1 && path <= _pCycles.Length) {
                return _pCycles[path - 1];
            } else {
                throw new IndexOutOfRangeException("Invalid path number");
            }
        }
        public void SetPlannedCyclesOnFirstProcess(int path, int numCycles)
        {
            if (path >= 1 && path <= _pCycles.Length) {
                _pCycles[path - 1] = numCycles;
            } else {
                throw new IndexOutOfRangeException("Invalid path number");
            }
        }

        //Simulated Starting Time
        public DateTime GetSimulatedStartingTimeUTC(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].SimulatedStartingUTC;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void SetSimulatedStartingTimeUTC(int process, int path, DateTime startUTC)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].SimulatedStartingUTC = startUTC;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public IEnumerable<SimulatedProduction> GetSimulatedProduction(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].SimulatedProduction;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void SetSimulatedProduction(int process, int path, IEnumerable<SimulatedProduction> prod)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].SimulatedProduction = new List<SimulatedProduction>(prod);
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public TimeSpan GetSimulatedAverageFlowTime(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].SimulatedAverageFlowTime;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void SetSimulatedAverageFlowTime(int process, int path, TimeSpan t)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].SimulatedAverageFlowTime = t;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }

        //pallet and fixture information
        public void AddProcessOnPallet(int process, int path, string pallet)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].Pallets.Add(pallet);
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void AddProcessOnFixture(int process, int path, string fixture, string face)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                var f = default(FixtureFace);
                f.Fixture = fixture;
                f.Face = face;
                _procPath[process - 1][path - 1].Fixtures.Add(f);
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public IEnumerable<string> PlannedPallets(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].Pallets;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public IEnumerable<FixtureFace> PlannedFixtures(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].Fixtures;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public IEnumerable<string> AllPlannedPallets()
        {
            var ret = new List<string>();
            for (int process = 0; process < NumProcesses; process++) {
                for (int path = 0; path < GetNumPaths(process + 1); path++) {
                    foreach (string pal in _procPath[process][path].Pallets) {
                        if (!ret.Contains(pal))
                            ret.Add(pal);
                    }
                }
            }
            return ret;
        }
        public bool HasPallet(int process, int path, string pallet)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].Pallets.Contains(pallet);
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public bool HasFixture(int process, int path, string fixture, string face)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                foreach (var f in _procPath[process - 1][path - 1].Fixtures) {
                    if (f.Fixture == fixture && f.Face == face)
                        return true;
                }
                return false;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        /* PartsPerPallet is here for situations when the fixture/face info in CellConfiguration is not used */
        public int PartsPerPallet(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].PartsPerPallet;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void SetPartsPerPallet(int process, int path, int partsPerPallet)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].PartsPerPallet = partsPerPallet;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }

        public IEnumerable<int> LoadStations(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].Load;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void AddLoadStation(int process, int path, int statNum)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].Load.Add(statNum);
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public IEnumerable<int> UnloadStations(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].Unload;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void AddUnloadStation(int process, int path, int statNum)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].Unload.Add(statNum);
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public IEnumerable<JobMachiningStop> GetMachiningStop(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].Stops;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void AddMachiningStop(int process, int path, JobMachiningStop r)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].Stops.Add(r);
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void AddMachiningStops(int process, int path, IEnumerable<JobMachiningStop> stops)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                foreach (var s in stops)
                    _procPath[process - 1][path - 1].Stops.Add(s);
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public string GetInputQueue(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].InputQueue;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public string GetOutputQueue(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].OutputQueue;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void SetInputQueue(int process, int path, string queue)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].InputQueue = queue;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void SetOutputQueue(int process, int path, string queue)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].OutputQueue = queue;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }

        //Inspection information
        public IEnumerable<JobInspectionData> GetInspections()
        {
            return _inspections;
        }
        public void AddInspection(JobInspectionData insp)
        {
            foreach (var i in _inspections) {
                if (insp.InspectionType == i.InspectionType)
                    throw new ArgumentException("Duplicate inspection types");
            }
            _inspections.Add(insp);
        }
        public void ReplaceInspection(JobInspectionData insp)
        {
            foreach (var i in _inspections) {
                if (insp.InspectionType == i.InspectionType) {
                    _inspections.Remove(i);
                    break;
                }
            }
            _inspections.Add(insp);
        }
        public void AddInspections(IEnumerable<JobInspectionData> insps)
        {
            foreach (var insp in insps)
                AddInspection(insp);
        }

        public JobPlan(string unique, int numProcess) : this(unique, numProcess, null)
        {
        }
        public JobPlan(string unique, int numProcess, int[] numPaths)
        {
            _routeStartUTC = DateTime.MinValue;
            _routeEndUTC = DateTime.MinValue;
            _archived = false;
            _copiedToSystem = false;
            _partName = "";
            _scheduleId = "";
            _uniqueStr = unique;
            _priority = 0;
            _comment = "";
            _manuallyCreated = false;
            _inspections = new List<JobInspectionData>();
            _holdJob = new JobHoldPattern();
            _scheduledIds = new List<string>();

            _procPath = new ProcPathInfo[numProcess][];
            for (int i = 0; i < numProcess; i++) {
                if (numPaths == null || i >= numPaths.Length) {
                    _procPath[i] = new ProcPathInfo[1];
                } else {
                    _procPath[i] = new ProcPathInfo[Math.Max(1, numPaths[i])];
                }
                for (int j = 0; j < _procPath[i].Length; j++) {
                    _procPath[i][j] = new ProcPathInfo(default(ProcPathInfo));
                }
            }
            _pCycles = new int[_procPath[0].Length];
            for (int path = 0; path < _procPath[0].Length; path++) {
                _pCycles[path] = 0;
            }
        }
        public JobPlan(JobPlan job, string newUniqueStr)
        {
            _routeStartUTC = job._routeStartUTC;
            _routeEndUTC = job._routeEndUTC;
            _archived = job._archived;
            _copiedToSystem = job._copiedToSystem;
            _partName = job.PartName;
            _scheduleId = job._scheduleId;
            _uniqueStr = newUniqueStr;
            _priority = job._priority;
            _comment = job._comment;
            _manuallyCreated = job._manuallyCreated;
            _createMarker = job._createMarker;
            _holdJob = new JobHoldPattern(job._holdJob);
            _scheduledIds = new List<string>(job._scheduledIds);

            _inspections = new List<JobInspectionData>(job._inspections.Count);
            foreach (var insp in job._inspections)
                _inspections.Add(new JobInspectionData(insp));

            //copy the path info
            _procPath = new ProcPathInfo[job._procPath.Length][];
            for (int i = 0; i < _procPath.Length; i++) {
                _procPath[i] = new ProcPathInfo[job._procPath[i].Length];
                for (int j = 0; j < _procPath[i].Length; j++) {
                    _procPath[i][j] = new ProcPathInfo(job._procPath[i][j]);
                }
            }

            //do not copy the planned cycles, since we are creating a new job
            _pCycles = new int[_procPath[0].Length];
            for (int path = 0; path < _procPath[0].Length; path++) {
                _pCycles[path] = 0;
            }
        }
        public JobPlan(JobPlan job)
        {
            _routeStartUTC = job._routeStartUTC;
            _routeEndUTC = job._routeEndUTC;
            _archived = job._archived;
            _copiedToSystem = job._copiedToSystem;
            _partName = job.PartName;
            _scheduleId = job._scheduleId;
            _uniqueStr = job._uniqueStr;
            _priority = job._priority;
            _comment = job._comment;
            _manuallyCreated = job._manuallyCreated;
            _createMarker = job._createMarker;
            _holdJob = new JobHoldPattern(job._holdJob);
            _scheduledIds = new List<string>(job._scheduledIds);

            _inspections = new List<JobInspectionData>(job._inspections.Count);
            foreach (var insp in job._inspections)
                _inspections.Add(new JobInspectionData(insp));

            _procPath = new ProcPathInfo[job._procPath.Length][];
            for (int i = 0; i < _procPath.Length; i++) {
                _procPath[i] = new ProcPathInfo[job._procPath[i].Length];
                for (int j = 0; j < _procPath[i].Length; j++) {
                    _procPath[i][j] = new ProcPathInfo(job._procPath[i][j]);
                }
            }
            _pCycles = new int[_procPath[0].Length];
            for (int path = 0; path < _procPath[0].Length; path++) {
                _pCycles[path] = job._pCycles[path];
            }
        }

        [DataMember(Name="RouteStartUTC")]
        private DateTime _routeStartUTC;

        [DataMember(Name="RouteEndUTC")]
        private DateTime _routeEndUTC;

        [DataMember(Name="Archived")]
        private bool _archived;

        [DataMember(Name="CopiedToSystem")]
        private bool _copiedToSystem;

        [DataMember(Name="PartName")]
        private string _partName;

        [DataMember(Name="Comment")]
        private string _comment;

        [DataMember(Name="Unique")]
        private string _uniqueStr;

        [DataMember(Name="Priority")]
        private int _priority;

        [DataMember(Name="ScheduleId", IsRequired=false)]
        private string _scheduleId;

        [DataMember(Name="Bookings")]
        private List<string> _scheduledIds;

        [DataMember(Name="ManuallyCreated")]
        private bool _manuallyCreated;

        [DataMember(Name="CreateMarkingData")]
        private bool _createMarker;

        [DataMember(Name = "Inspections")]
        private IList<JobInspectionData> _inspections;

        [DataMember(Name="HoldEntireJob")]
        private JobHoldPattern _holdJob;

        [DataMember(Name="CyclesOnFirstProcess")]
        private int[] _pCycles;

        [Serializable, DataContract]
        public struct FixtureFace : IComparable<FixtureFace>
        {
            [DataMember] public string Fixture;
            [DataMember] public string Face;

            public int CompareTo(FixtureFace o)
            {
                var i = Fixture.CompareTo(o.Fixture);
                if (i < 0) return -1;
                if (i > 0) return 1;
                return Face.CompareTo(o.Face);
            }

            public override string ToString()
            {
                return Fixture + ":" + Face;
            }
        }

        [Serializable, DataContract]
        public struct SimulatedProduction
        {
            [DataMember] public DateTime TimeUTC;
            [DataMember] public int Quantity; //total quantity simulated to be completed at TimeUTC
        }

        [Serializable, DataContract]
        private struct ProcPathInfo
        {
            [DataMember] public int PathGroup;
            [DataMember] public IList<string> Pallets;
            [DataMember] public IList<FixtureFace> Fixtures;
            [DataMember] public IList<int> Load;
            [DataMember] public IList<int> Unload;
            [DataMember] public IList<JobMachiningStop> Stops;
            [DataMember] public IList<SimulatedProduction> SimulatedProduction;
            [DataMember] public DateTime SimulatedStartingUTC;
            [DataMember] public TimeSpan SimulatedAverageFlowTime; // average time a part takes to complete the entire sequence
            [DataMember] public JobHoldPattern HoldMachining;
            [DataMember] public JobHoldPattern HoldLoadUnload;
            [DataMember] public int PartsPerPallet;
            [DataMember(IsRequired = false), OptionalField] public string InputQueue;
            [DataMember(IsRequired = false), OptionalField] public string OutputQueue;

            public ProcPathInfo(ProcPathInfo other)
            {
                if (other.Pallets == null) {
                    PathGroup = 0;
                    Pallets = new List<string>();
                    Fixtures = new List<FixtureFace>();
                    Load = new List<int>();
                    Unload = new List<int>();
                    Stops = new List<JobMachiningStop>();
                    SimulatedProduction = new List<SimulatedProduction>();
                    SimulatedStartingUTC = DateTime.MinValue;
                    SimulatedAverageFlowTime = TimeSpan.Zero;
                    HoldMachining = new JobHoldPattern();
                    HoldLoadUnload = new JobHoldPattern();
                    PartsPerPallet = 1;
                    InputQueue = null;
                    OutputQueue = null;
                } else {
                    PathGroup = other.PathGroup;
                    Pallets = new List<string>(other.Pallets);
                    Fixtures = new List<FixtureFace>(other.Fixtures);
                    Load = new List<int>(other.Load);
                    Unload = new List<int>(other.Unload);
                    Stops = new List<JobMachiningStop>();
                    SimulatedProduction = new List<SimulatedProduction>(other.SimulatedProduction);
                    SimulatedStartingUTC = other.SimulatedStartingUTC;
                    SimulatedAverageFlowTime = other.SimulatedAverageFlowTime;
                    foreach (var s in other.Stops) {
                        Stops.Add(new JobMachiningStop(s));
                    }
                    HoldMachining = new JobHoldPattern(other.HoldMachining);
                    HoldLoadUnload = new JobHoldPattern(other.HoldLoadUnload);
                    PartsPerPallet = other.PartsPerPallet;
                    InputQueue = other.InputQueue;
                    OutputQueue = other.OutputQueue;
                }
            }
        }

        [DataMember(Name="ProcsAndPaths")]
        private ProcPathInfo[][] _procPath;
    }

    [SerializableAttribute, DataContract]
    public class JobCurrentInformation : JobPlan
    {
        [SerializableAttribute, DataContract]
        public struct Material
        {
            //can be -1 if the job manager does not track material
            [DataMember] public readonly long MaterialId;
            [DataMember] public readonly int Process;
            [DataMember] public readonly int Path;

            [DataMember] public readonly string Pallet;
            [DataMember] public readonly string Fixture;
            [DataMember] public readonly string FaceName;

            public Material(long matID, int proc, int path, string pal, string fix, string face)
            {
                MaterialId = matID;
                Process = proc;
                Path = path;
                Pallet = pal;
                Fixture = fix;
                FaceName = face;
            }
        }

        public IList<Material> MaterialInExecution
        {
            get { return _material; }
        }
        public void AddMaterial(Material mat)
        {
            _material.Add(mat);
        }

        public int GetCompletedOnFirstProcess(int path)
        {
            if (path >= 1 && path <= GetNumPaths(1)) {
                return _completedProc1[path - 1];
            } else {
                throw new IndexOutOfRangeException("Invalid path number");
            }
        }
        public void SetCompletedOnFirstProcess(int path, int comp)
        {
            if (path >= 1 && path <= GetNumPaths(1)) {
                _completedProc1[path - 1] = comp;
            } else {
                throw new IndexOutOfRangeException("Invalid path number");
            }
        }
        public int GetCompletedOnFinalProcess()
        {
            return _totalComplete;
        }
        public void SetCompletedOnFinalProcess(int comp)
        {
            _totalComplete = comp;
        }
        public void IncrementCompletedOnFinalProcess(int comp)
        {
            _totalComplete += comp;
        }

        public JobCurrentInformation(string unique, int numProc) : this(unique, numProc, null)
        {

        }
        public JobCurrentInformation(string unique, int numProc, int[] numPaths) : base(unique, numProc, numPaths)
        {
            _material = new List<Material>();
            _completedProc1 = new int[GetNumPaths(1)];
            for (int i = 0; i < _completedProc1.Length; i++)
                _completedProc1[i] = 0;
            _totalComplete = 0;
        }
        public JobCurrentInformation(JobPlan job) : base(job)
        {
            _material = new List<Material>();
            _completedProc1 = new int[GetNumPaths(1)];
            for (int i = 0; i < _completedProc1.Length; i++)
                _completedProc1[i] = 0;
            _totalComplete = 0;
        }

        [DataMember(Name="Material")] private List<Material> _material;
        [DataMember(Name="CompletedProc1")] private int[] _completedProc1;
        [DataMember(Name="TotalComplete")] private int _totalComplete;
    }

    [SerializableAttribute, DataContract]
    public class CurrentStatus
    {
        public IDictionary<string, JobCurrentInformation> Jobs
        {
            get { return _jobs; }
        }

        public IDictionary<string, PalletStatus> Pallets
        {
            get { return _pals; }
        }

        public IList<string> Alarms
        {
            get { return _alarms; }
        }

        [DataMember] public string LatestScheduleId {get;}

        public IDictionary<string, int> ExtraPartsForLatestSchedule
        {
            get { return _extraParts; }
        }

        public IDictionary<string, QueueSize> QueueSizes => _queueSizes;

        public CurrentStatus(IEnumerable<JobCurrentInformation> jobs,
                             IDictionary<string, PalletStatus> pals,
                             string latestSchId,
                             IDictionary<string, int> extraParts)
        {
            _jobs = new Dictionary<string, JobCurrentInformation>();
            _pals = new Dictionary<string, PalletStatus>(pals);
            _alarms = new List<string>();
            LatestScheduleId = latestSchId;
            _extraParts = new Dictionary<string, int>(extraParts);

            foreach (var j in jobs)
                _jobs.Add(j.UniqueStr, j);
        }

        public CurrentStatus(IDictionary<string, JobCurrentInformation> jobs,
                             IDictionary<string, PalletStatus> pals,
                             string latestSchId,
                             IDictionary<string, int> extraParts,
                             IDictionary<string, QueueSize> queues)
        {
            _jobs = new Dictionary<string, JobCurrentInformation>(jobs);
            _pals = new Dictionary<string, PalletStatus>(pals);
            _alarms = new List<string>();
            LatestScheduleId = latestSchId;
            _extraParts = new Dictionary<string, int>(extraParts);
            _queueSizes = new Dictionary<string, QueueSize>(queues);
        }

        [DataMember(Name="Jobs")]
        private Dictionary<string, JobCurrentInformation> _jobs;
        [DataMember(Name="Pallets")]
        private Dictionary<string, PalletStatus> _pals;
        [DataMember(Name="Alarms")]
        private List<string> _alarms;
        [DataMember(Name="ExtraParts")]
        private Dictionary<string, int> _extraParts;
        [OptionalField, DataMember(IsRequired=false, Name="QueueSizes")]
        private Dictionary<string, QueueSize> _queueSizes;
    }

    [SerializableAttribute, DataContract]
    public class SimulatedStationUtilization
    {
        [DataMember] public string SimulationId; // a unique string shared between all utilization structs that are part of the same simulation
        [DataMember] public string StationGroup;
        [DataMember] public int StationNum;
        [DataMember] public DateTime StartUTC;
        [DataMember] public DateTime EndUTC;
        [DataMember] public TimeSpan UtilizationTime; //time between StartUTC and EndUTC the station is busy.
        [DataMember] public TimeSpan PlannedDownTime; //time between StartUTC and EndUTC the station is planned to be down.

        public SimulatedStationUtilization(string id, string group, int num, DateTime start, DateTime endT, TimeSpan u, TimeSpan d)
        {
            SimulationId = id;
            StationGroup = group;
            StationNum = num;
            StartUTC = start;
            EndUTC = endT;
            UtilizationTime = u;
            PlannedDownTime = d;
        }
    }

    [Serializable, DataContract]
    public class PartWorkorder
    {
        [DataMember] public string WorkorderId {get;set;}
        [DataMember] public string Part {get;set;}
        [DataMember] public int Quantity {get;set;}
        [DataMember] public DateTime DueDate {get;set;}
        [DataMember] public int Priority {get;set;}
    }

    [Serializable, DataContract]
    public struct QueueSize
    {
        //once an output queue grows to this size, stop loading new parts
        //which are destined for this queue
        [DataMember] public int MaxSizeBeforeStopLoading {get;set;}

        //once an output queue grows to this size, stop unloading parts
        //and keep them in the buffer inside the cell
        [DataMember] public int MaxSizeBeforeStopUnloading {get;set;}
    }

    [Serializable, DataContract]
    public struct NewJobs
    {
        [DataMember] public string ScheduleId;
        [DataMember] public List<JobPlan> Jobs;
        [DataMember] public List<SimulatedStationUtilization> StationUse;
        [DataMember] public Dictionary<string, int> ExtraParts;
        [DataMember] public bool ArchiveCompletedJobs;

        [OptionalField, DataMember(IsRequired=false)]
        public byte[] DebugMessage;

        [OptionalField, DataMember(IsRequired=false)]
        public List<PartWorkorder> CurrentUnfilledWorkorders;

        [OptionalField, DataMember(IsRequired=false)]
        public Dictionary<string, QueueSize> QueueSizes;

        public NewJobs(string scheduleId,
                       IEnumerable<JobPlan> newJobs,
                       IEnumerable<SimulatedStationUtilization> stationUse = null,
                       Dictionary<string, int> extraParts = null,
                       bool archiveCompletedJobs = false,
                       byte[] debugMsg = null,
                       IEnumerable<PartWorkorder> workorders = null,
                       Dictionary<string, QueueSize> queues = null)
        {
            this.ScheduleId = scheduleId;
            this.Jobs = newJobs.ToList();
            this.StationUse =
                stationUse == null ? new List<SimulatedStationUtilization>() : stationUse.ToList();
            this.ExtraParts =
                extraParts == null ? new Dictionary<string, int>() : extraParts;
            this.ArchiveCompletedJobs = archiveCompletedJobs;
            this.DebugMessage = debugMsg;
            this.CurrentUnfilledWorkorders = workorders?.ToList();
            this.QueueSizes =
                queues == null ? new Dictionary<string, QueueSize>() : queues;
        }
    }

    [Serializable, DataContract]
    public class JobAndPath : IEquatable<JobAndPath>
    {
        [DataMember] public readonly string UniqueStr;
        [DataMember] public readonly int Path;

        public JobAndPath(string unique, int path)
        {
            UniqueStr = unique;
            Path = path;
        }

        public bool Equals(JobAndPath other)
        {
            return (UniqueStr == other.UniqueStr && Path == other.Path);
        }
    }

    [Serializable, DataContract]
    public struct HistoricData
    {
        [DataMember] public IDictionary<string, JobPlan> Jobs;
        [DataMember] public ICollection<SimulatedStationUtilization> StationUse;
    }

    [Serializable, DataContract]
    public struct JobsAndExtraParts
    {
        [DataMember] public string LatestScheduleId;
        [DataMember] public List<JobPlan> Jobs;
        [DataMember] public Dictionary<string, int> ExtraParts;

        [OptionalField, DataMember(IsRequired=false)]
        public List<PartWorkorder> CurrentUnfilledWorkorders;
    }

    [Serializable, DataContract]
    public struct JobAndDecrementQuantity
    {
        [DataMember] public string DecrementId;
        [DataMember] public string JobUnique;
        [DataMember] public DateTime TimeUTC;
        [DataMember] public string Part;
        [DataMember] public int Quantity;
    }
}
