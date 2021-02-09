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
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Linq;
using Germinate;
using System.Collections.Immutable;

namespace BlackMaple.MachineWatchInterface
{
  [Serializable, DataContract]
  public class LogMaterial
  {
    [DataMember(Name = "id", IsRequired = true)]
    public long MaterialID { get; private set; }

    [DataMember(Name = "uniq", IsRequired = true)]
    public string JobUniqueStr { get; private set; }

    [DataMember(Name = "part", IsRequired = true)]
    public string PartName { get; private set; }

    [DataMember(Name = "proc", IsRequired = true)]
    public int Process { get; private set; }

    [DataMember(Name = "numproc", IsRequired = true)]
    public int NumProcesses { get; private set; }

    [DataMember(Name = "face", IsRequired = true)]
    public string Face { get; private set; }

    [DataMember(Name = "serial", IsRequired = false, EmitDefaultValue = false)]
    public string Serial { get; private set; }

    [DataMember(Name = "workorder", IsRequired = false, EmitDefaultValue = false)]
    public string Workorder { get; private set; }

    public LogMaterial(long matID, string uniq, int proc, string part, int numProc, string serial, string workorder, string face)
    {
      MaterialID = matID;
      JobUniqueStr = uniq;
      PartName = part;
      Process = proc;
      NumProcesses = numProc;
      Face = face;
      Serial = serial;
      Workorder = workorder;
    }

    private LogMaterial() { } //for json deserialization
  }

  [Serializable, DataContract]
  public enum LogType
  {
    [EnumMember] LoadUnloadCycle = 1, //numbers are for backwards compatibility with old type enumeration
    [EnumMember] MachineCycle = 2,
    [EnumMember] PartMark = 6,
    [EnumMember] Inspection = 7,
    [EnumMember] OrderAssignment = 10,
    [EnumMember] GeneralMessage = 100,
    [EnumMember] PalletCycle = 101,
    [EnumMember] FinalizeWorkorder = 102,
    [EnumMember] InspectionResult = 103,
    [EnumMember] Wash = 104,
    [EnumMember] AddToQueue = 105,
    [EnumMember] RemoveFromQueue = 106,
    [EnumMember] InspectionForce = 107,
    [EnumMember] PalletOnRotaryInbound = 108,
    [EnumMember] PalletInStocker = 110,
    [EnumMember] SignalQuarantine = 111,
    [EnumMember] InvalidateCycle = 112,
    [EnumMember] SwapMaterialOnPallet = 113,
    // when adding types, must also update the convertLogType() function in client/backup-viewer/src/background.ts
  }

  [Serializable, DataContract, KnownType(typeof(MaterialProcessActualPath))]
  public class LogEntry
  {
    [DataMember(Name = "counter", IsRequired = true)]
    public long Counter { get; private set; }

    [DataMember(Name = "material", IsRequired = true)]
    public IEnumerable<LogMaterial> Material { get; private set; }

    [DataMember(Name = "type", IsRequired = true)]
    public LogType LogType { get; private set; }

    [DataMember(Name = "startofcycle", IsRequired = true)]
    public bool StartOfCycle { get; private set; }

    [DataMember(Name = "endUTC", IsRequired = true)]
    public DateTime EndTimeUTC { get; private set; }

    [DataMember(Name = "loc", IsRequired = true)]
    public string LocationName { get; private set; }

    [DataMember(Name = "locnum", IsRequired = true)]
    public int LocationNum { get; private set; }

    [DataMember(Name = "pal", IsRequired = true)]
    public string Pallet { get; private set; }

    [DataMember(Name = "program", IsRequired = true)]
    public string Program { get; private set; }

    [DataMember(Name = "result", IsRequired = true)]
    public string Result { get; private set; }

    // End of route is kept only for backwards compatbility.
    // Instead, the user who is processing the data should determine what event
    // to use to determine when the material should be considered "complete"
    [IgnoreDataMember]
    public bool EndOfRoute { get; private set; }

    [DataMember(Name = "elapsed", IsRequired = true)]
    public TimeSpan ElapsedTime { get; private set; } //time from cycle-start to cycle-stop

    [DataMember(Name = "active", IsRequired = true)]
    public TimeSpan ActiveOperationTime { get; private set; } //time that the machining or operation is actually active

    [DataMember(Name = "details", IsRequired = false, EmitDefaultValue = false)]
    private Dictionary<string, string> _details;
    public IDictionary<string, string> ProgramDetails { get { return _details; } }

    [DataMember(Name = "tools", IsRequired = false, EmitDefaultValue = false)]
    public IDictionary<string, ToolUse> Tools { get; private set; }

    public LogEntry(
        long cntr,
        IEnumerable<LogMaterial> mat,
        string pal,
        LogType ty,
        string locName,
        int locNum,
        string prog,
        bool start,
        DateTime endTime,
        string result,
        bool endOfRoute)
        : this(cntr, mat, pal, ty, locName, locNum, prog, start, endTime, result, endOfRoute,
              TimeSpan.FromMinutes(-1), TimeSpan.Zero)
    { }

    public LogEntry(
        long cntr,
        IEnumerable<LogMaterial> mat,
        string pal,
        LogType ty,
        string locName,
        int locNum,
        string prog,
        bool start,
        DateTime endTime,
        string result,
        bool endOfRoute,
        TimeSpan elapsed,
        TimeSpan active)
    {
      Counter = cntr;
      Material = mat; // ok since material is immutable
      Pallet = pal;
      LogType = ty;
      LocationName = locName;
      LocationNum = locNum;
      Program = prog;
      StartOfCycle = start;
      EndTimeUTC = endTime;
      Result = result;
      EndOfRoute = endOfRoute;
      ElapsedTime = elapsed;
      ActiveOperationTime = active;
      _details = new Dictionary<string, string>();
      Tools = new Dictionary<string, ToolUse>();
    }

    public LogEntry(LogEntry copy, long newCounter)
    {
      Counter = newCounter;
      Material = copy.Material; // ok since material is immutable
      Pallet = copy.Pallet;
      LogType = copy.LogType;
      LocationName = copy.LocationName;
      LocationNum = copy.LocationNum;
      Program = copy.Program;
      StartOfCycle = copy.StartOfCycle;
      EndTimeUTC = copy.EndTimeUTC;
      Result = copy.Result;
      EndOfRoute = copy.EndOfRoute;
      ElapsedTime = copy.ElapsedTime;
      ActiveOperationTime = copy.ActiveOperationTime;
      _details = new Dictionary<string, string>(copy._details);
      Tools = new Dictionary<string, ToolUse>(copy.Tools);
    }

    public LogEntry(LogEntry copy, IEnumerable<LogMaterial> newMats)
    {
      Counter = copy.Counter;
      Material = newMats;
      Pallet = copy.Pallet;
      LogType = copy.LogType;
      LocationName = copy.LocationName;
      LocationNum = copy.LocationNum;
      Program = copy.Program;
      StartOfCycle = copy.StartOfCycle;
      EndTimeUTC = copy.EndTimeUTC;
      Result = copy.Result;
      EndOfRoute = copy.EndOfRoute;
      ElapsedTime = copy.ElapsedTime;
      ActiveOperationTime = copy.ActiveOperationTime;
      _details = new Dictionary<string, string>(copy._details);
      Tools = new Dictionary<string, ToolUse>(copy.Tools);
    }

    public LogEntry(LogEntry copy) : this(copy, copy.Counter) { }

    private LogEntry() { } //for json deserialization

    public bool ShouldSerializeProgramDetails()
    {
      return _details.Count > 0;
    }

    public bool ShouldSerializeTools()
    {
      return Tools.Count > 0;
    }
  }

  [DataContract, Serializable]
  public record MaterialDetails
  {
    [DataMember(IsRequired = true)] public long MaterialID { get; init; }
    [DataMember] public string JobUnique { get; init; }
    [DataMember] public string PartName { get; init; }
    [DataMember] public int NumProcesses { get; init; }
    [DataMember] public string Workorder { get; init; }
    [DataMember] public string Serial { get; init; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableDictionary<int, int> Paths { get; init; } = ImmutableDictionary<int, int>.Empty; // key is process, value is path
  }

  [DataContract, Draftable]
  public record ToolUse
  {
    [DataMember(IsRequired = true)] public TimeSpan ToolUseDuringCycle { get; init; }
    [DataMember(IsRequired = true)] public TimeSpan TotalToolUseAtEndOfCycle { get; init; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public TimeSpan ConfiguredToolLife { get; init; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public bool? ToolChangeOccurred { get; init; }

    public static ToolUse operator %(ToolUse t, Action<IToolUseDraft> f)
       => t.Produce(f);
  }

  // stored serialized in json format in the details for inspection logs.
  [DataContract, Draftable]
  public record MaterialProcessActualPath
  {
    [DataContract]
    public record Stop
    {
      [DataMember(IsRequired = true)] public string StationName { get; init; }
      [DataMember(IsRequired = true)] public int StationNum { get; init; }
    }

    [DataMember(IsRequired = true)] public long MaterialID { get; init; }
    [DataMember(IsRequired = true)] public int Process { get; init; }
    [DataMember(IsRequired = true)] public string Pallet { get; init; }
    [DataMember(IsRequired = true)] public int LoadStation { get; init; }
    [DataMember(IsRequired = true)] public ImmutableList<Stop> Stops { get; init; } = ImmutableList<Stop>.Empty;
    [DataMember(IsRequired = true)] public int UnloadStation { get; init; }

    public static MaterialProcessActualPath operator %(MaterialProcessActualPath m, Action<IMaterialProcessActualPathDraft> f)
       => m.Produce(f);
  }


  [DataContract, Draftable]
  public record WorkorderPartSummary
  {
    [DataMember(Name = "name", IsRequired = true)]
    public string Part { get; init; }

    [DataMember(Name = "completed-qty", IsRequired = true)]
    public int PartsCompleted { get; init; }

    [DataMember(Name = "elapsed-station-time", IsRequired = true)]
    public ImmutableDictionary<string, TimeSpan> ElapsedStationTime { get; init; } = ImmutableDictionary<string, TimeSpan>.Empty;

    [DataMember(Name = "active-stat-time", IsRequired = true)]
    public ImmutableDictionary<string, TimeSpan> ActiveStationTime { get; init; } = ImmutableDictionary<string, TimeSpan>.Empty;

    public static WorkorderPartSummary operator %(WorkorderPartSummary m, Action<IWorkorderPartSummaryDraft> f)
       => m.Produce(f);
  }

  [DataContract, Draftable]
  public record WorkorderSummary
  {
    [DataMember(Name = "id", IsRequired = true)]
    public string WorkorderId { get; init; }

    [DataMember(Name = "parts", IsRequired = true)]
    public ImmutableList<WorkorderPartSummary> Parts { get; init; } = ImmutableList<WorkorderPartSummary>.Empty;

    [DataMember(Name = "serials", IsRequired = true)]
    public ImmutableList<string> Serials { get; init; } = ImmutableList<string>.Empty;

    [DataMember(Name = "finalized", IsRequired = false, EmitDefaultValue = false)]
    public DateTime? FinalizedTimeUTC { get; init; }

    public static WorkorderSummary operator %(WorkorderSummary m, Action<IWorkorderSummaryDraft> f)
       => m.Produce(f);
  }

  [DataContract]
  public record EditMaterialInLogEvents
  {
    [DataMember(IsRequired = true)]
    public long OldMaterialID { get; init; }

    [DataMember(IsRequired = true)]
    public long NewMaterialID { get; init; }

    [DataMember(IsRequired = true)]
    public IEnumerable<LogEntry> EditedEvents { get; init; }
  }
}
