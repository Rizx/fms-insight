﻿/* Copyright (c) 2018, John Lenz

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
using System.Text;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;

namespace Cincron
{
    public class MessageWatcher
    {
        private JobLogDB _log;
        private string _msgFile;
        private object _lock;
        private System.Timers.Timer _timer;
        private System.Diagnostics.TraceSource _trace;

        public MessageWatcher(string msgFile, JobLogDB log, System.Diagnostics.TraceSource trace)
        {
            _msgFile = msgFile;
            _trace = trace;
            _log = log;
            _lock = new object();
            _timer = new System.Timers.Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
            _timer.Elapsed += CheckMessages;
        }

        public void Start()
        {
            _timer.Start();
        }

        public void Halt()
        {
            _timer.Stop();
        }

        #region "Message Timer"
        //State accumulated as we walk through the messages
        private class MessageState
        {
            public CincronMessage LastSeenMessage;
            public List<CincronMessage.PartCompleted> PartCompletedMessages
                = new List<CincronMessage.PartCompleted>();
            public PalletLocation LastUnloadStation;
        }

        public void CheckMessages(object sender, System.Timers.ElapsedEventArgs e)
        {
            lock (_lock) {
                try {
                    _trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                        "Starting to read " + _msgFile);

                    var msgs = LoadMessages();
                    var state = new MessageState();

                    for (int i = 0; i < msgs.Count; i++) {
                        var msg = msgs[i];
                        int repeatCount = 1;
                        if (i < msgs.Count - 1) {
                            var nextMsg = msgs[i + 1];
                            if (nextMsg is CincronMessage.PreviousMessageRepeated) {
                                repeatCount = ((CincronMessage.PreviousMessageRepeated)nextMsg).NumRepeated + 1;
                            }
                        }
                        _trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                           "Processing new message " + msg.ToString() + " repeated " + repeatCount.ToString());

                        HandleMessage(state, msg, repeatCount);

                        state.LastSeenMessage = msg;
                    }

                    //TODO: store state to filesystem
                    //for now, the next time this runs the events will be re-processed

                } catch (Exception ex) {
                    _trace.TraceEvent(System.Diagnostics.TraceEventType.Error, 0,
                        "Unhandled error in message file watcher " + ex.ToString());
                }
            }
        }

        private IList<CincronMessage> LoadMessages()
        {
            var max = _log.MaxForeignID();
            if (max != null && max != "") {
                var expected = _log.OriginalMessageByForeignID(max);
                if (expected != null && expected != "") {
                    var maxParts = max.Split('-'); //year-month-day-hour-min-sec-fileoffset
                    int offset;
                    if (maxParts.Length >= 7 && int.TryParse(maxParts[6], out offset)) {
                        _trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                            "Starting reading at offset " + offset.ToString() + " with message " + expected.ToString());
                        return MessageParser.ExtractMessages(_msgFile, offset, expected, _trace);
                    }
                }
            }

            _trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                "Starting reading message file from beginning");
            return MessageParser.ExtractMessages(_msgFile, 0, "", _trace);
        }
        #endregion

        #region "Single Message Processing"
        private void HandleMessage(MessageState state, CincronMessage msg, int repeatCount)
        {
            var queueChange = msg as CincronMessage.QueuePositionChange;

            //machine cycle start.  For now it is pallet rotating into machine
            if (queueChange != null
                && queueChange.CurrentLocation.Location == PalletLocationEnum.Machine
                && queueChange.NewQueuePosition == "10010") {

                _log.AddStationCycle(new LogEntry(
                     cntr: 0,
                     mat: FindMaterial(queueChange.Pallet),
                     pal: queueChange.Pallet,
                     ty: LogType.MachineCycle,
                     locName: "MC",
                     locNum: queueChange.CurrentLocation.Num,
                     prog: "",
                     start: true,
                     endTime: queueChange.TimeUTC,
                     result: "",
                     endOfRoute: false),
                     ForeignId(msg), msg.LogMessage);
            }

            //machine cycle end.  StepNo changing to 5 signals cycle end.
            var stepChange = msg as CincronMessage.PartNewStep;
            if (stepChange != null && stepChange.StepNo == 5) {
                var machineCycleStart = FindMachineStart(_log.CurrentPalletLog(stepChange.Pallet));

                if (machineCycleStart != null) {
                    _log.AddStationCycle(new LogEntry(
                        cntr: 0,
                        mat: machineCycleStart.Material,
                        pal: stepChange.Pallet,
                        ty: LogType.MachineCycle,
                        locName: "MC",
                        locNum: machineCycleStart.LocationNum,
                        prog: "",
                        start: false,
                        endTime: stepChange.TimeUTC,
                        result: "",
                        endOfRoute: false,
                        elapsed: stepChange.TimeUTC.Subtract(machineCycleStart.EndTimeUTC),
                        active: TimeSpan.Zero),
                     ForeignId(msg), msg.LogMessage);
                }
            }

            //program end.  FindMachineStart correctly returns null if we have already recorded
            //cycle end.
            var progEnd = msg as CincronMessage.ProgramFinished;
            if (progEnd != null)
            {
                var machineCycleStart = FindMachineStart(_log.CurrentPalletLog(progEnd.Pallet));

                if (machineCycleStart != null)
                {
                    _log.AddStationCycle(new LogEntry(
                        cntr: 0,
                        mat: machineCycleStart.Material,
                        pal: progEnd.Pallet,
                        ty: LogType.MachineCycle,
                        locName: "MC",
                        locNum: machineCycleStart.LocationNum,
                        prog: "",
                        start: false,
                        endTime: progEnd.TimeUTC,
                        result: "",
                        endOfRoute: false,
                        elapsed: progEnd.TimeUTC.Subtract(machineCycleStart.EndTimeUTC),
                        active: TimeSpan.Zero),
                     ForeignId(msg), msg.LogMessage);
                }
            }

            //part completed message.  Store in memory since typically there is an Unload Start event
            //which happens right afterwords.
            var comp = msg as CincronMessage.PartCompleted;
            if (comp != null) {
                for (int i = 0; i < repeatCount; i++) {
                    state.PartCompletedMessages.Add(comp);
                }
            }

            //move to unload.  Store in memory, typically there is an UnloadStart event soon
            if (queueChange != null
                && queueChange.CurrentLocation.Location == PalletLocationEnum.LoadUnload
                && queueChange.NewQueuePosition == "10010") {
                state.LastUnloadStation = queueChange.CurrentLocation;
            }

            //unload start.  Use the completed parts and last unload station from the state.
            var unloadStart = msg as CincronMessage.PartUnloadStart;
            if (unloadStart != null) {
                _log.AddStationCycle(new LogEntry(
                   cntr: 0,
                   mat: CreateUnloadMaterial(state, unloadStart.Pallet),
                   pal: unloadStart.Pallet,
                   ty: LogType.LoadUnloadCycle,
                   locName: "Load",
                   locNum: state.LastUnloadStation.Num,
                   prog: "",
                   start: true,
                   endTime: unloadStart.TimeUTC,
                   result: "UNLOAD",
                   endOfRoute: false),
                ForeignId(msg), msg.LogMessage);
                state.PartCompletedMessages.Clear();
            }

            var loadStart = msg as CincronMessage.PartLoadStart;
            if (loadStart != null) {
                _log.AddStationCycle(new LogEntry(
                   cntr: 0,
                   mat: CreateLoadMaterial(loadStart),
                   pal: loadStart.Pallet,
                   ty: LogType.LoadUnloadCycle,
                   locName: "Load",
                   locNum: state.LastUnloadStation.Num,
                   prog: "",
                   start: true,
                   endTime: loadStart.TimeUTC,
                   result: "LOAD",
                   endOfRoute: false),
                ForeignId(msg), msg.LogMessage);
            }

            //end of load and unload on step change to 2
            if (stepChange != null && stepChange.StepNo == 2) {

                //create end unload, then pallet cycle, then end load.
                var oldEvts = _log.CurrentPalletLog(stepChange.Pallet);
                var loadStartCycle = FindLoadStart(oldEvts);
                var unloadStartCycle = FindUnloadStart(oldEvts);
                var newEvts = new List<LogEntry>();

                if (unloadStartCycle != null) {
                    newEvts.Add(new LogEntry(
                      cntr: 0,
                      mat: unloadStartCycle.Material,
                      pal: stepChange.Pallet,
                      ty: LogType.LoadUnloadCycle,
                      locName: "Load",
                      locNum: unloadStartCycle.LocationNum,
                      prog: "",
                      start: false,
                      endTime: stepChange.TimeUTC,
                      result: "UNLOAD",
                      endOfRoute: true,
                      elapsed: stepChange.TimeUTC.Subtract(unloadStartCycle.EndTimeUTC),
                      active: TimeSpan.Zero));
                }

                //pallet cycle
                var lastCycleTime = _log.LastPalletCycleTime(stepChange.Pallet);
                var elapsed = TimeSpan.Zero;
                if (lastCycleTime > DateTime.MinValue)
                    elapsed = stepChange.TimeUTC.Subtract(lastCycleTime);
                newEvts.Add(new LogEntry(
                  cntr: 0,
                  mat: new List<LogMaterial>(),
                  pal: stepChange.Pallet,
                  ty: LogType.PalletCycle,
                  locName: "Pallet Cycle",
                  locNum: 1,
                  prog: "",
                  start: false,
                  endTime: stepChange.TimeUTC,
                  result: "PalletCycle",
                  endOfRoute: false,
                  elapsed: elapsed,
                  active: TimeSpan.Zero));

                //end load, one second after pallet cycle
                if (loadStartCycle != null) {
                    newEvts.Add(new LogEntry(
                      cntr: 0,
                      mat: loadStartCycle.Material,
                      pal: stepChange.Pallet,
                      ty: LogType.LoadUnloadCycle,
                      locName: "Load",
                      locNum: loadStartCycle.LocationNum,
                      prog: "",
                      start: false,
                      endTime: stepChange.TimeUTC.AddSeconds(1),
                      result: "LOAD",
                      endOfRoute: true,
                      elapsed: stepChange.TimeUTC.Subtract(loadStartCycle.EndTimeUTC),
                      active: TimeSpan.Zero));
                }
                _log.AddStationCycles(newEvts, ForeignId(msg), msg.LogMessage);
            }
        }

        private string ForeignId(CincronMessage msg)
        {
            return msg.TimeOfFirstEntryInLogFileUTC.ToString("yyyy-MM-dd-HH-mm-ss") + "-" + msg.LogFileOffset.ToString("000000000000");
        }

        private LogEntry FindMachineStart(IList<LogEntry> oldEvents)
        {
            LogEntry ret = null;
            foreach (var c in oldEvents) {
                if (c.LogType == LogType.MachineCycle && c.StartOfCycle)
                    ret = c;
                if (c.LogType == LogType.MachineCycle && !c.StartOfCycle)
                    return null;  //immedietly return null because there is already an end
            }
            return ret;
        }

        private LogEntry FindUnloadStart(IList<LogEntry> oldEvents)
        {
            foreach (var c in oldEvents) {
                if (c.LogType == LogType.LoadUnloadCycle
                    && c.StartOfCycle
                    && c.Result == "UNLOAD")
                    return c;
            }
            return null;
        }

        private LogEntry FindLoadStart(IList<LogEntry> oldEvents)
        {
            foreach (var c in oldEvents) {
                if (c.LogType == LogType.LoadUnloadCycle
                    && c.StartOfCycle
                    && c.Result == "LOAD")
                    return c;
            }
            return null;
        }
        #endregion

        #region "Material"
        //since we don't know the quantity on the pallet until the very end, can just
        //create a single material ID.  At the very end, when we do know the count (and part name),
        //can add exactly that many material ids as long as the original material id is included.

        private IEnumerable<LogMaterial> CreateLoadMaterial(CincronMessage.PartLoadStart load)
        {
            var matId = _log.AllocateMaterialID(load.WorkId);
            _trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                 "Creating new material id for load event: " + matId.ToString() +
                 " work id " + load.WorkId);
            return new LogMaterial[] {
                new LogMaterial(
                    matID: matId,
                    uniq: load.WorkId,
                    proc: 1,
                    part: "",
                    numProc: 1)
            };
        }

        private IList<LogMaterial> FindMaterial(string pal)
        {
            var oldEvts = _log.CurrentPalletLog(pal);
            for (int i = oldEvts.Count - 1; i >= 0; i--) {
                if (oldEvts[i].Material.Count() > 0) {
                    return new List<LogMaterial>(oldEvts[i].Material);
                }
            }

            _trace.TraceEvent(System.Diagnostics.TraceEventType.Warning, 0,
                "Unable to find existing material for pallet " + pal);
            var matId = _log.AllocateMaterialID("");
            return new LogMaterial[] {
                new LogMaterial(
                    matID: matId,
                    uniq: "",
                    proc: 1,
                    part: "",
                    numProc: 1)
                };
        }

        private IEnumerable<LogMaterial> CreateUnloadMaterial(MessageState state, string pal)
        {
            var oldMat = FindMaterial(pal)[0];
            var ret = new List<LogMaterial>();
            string partName = "";
            if (state.PartCompletedMessages.Count > 0)
                partName = state.PartCompletedMessages[0].PartName;

            ret.Add(new LogMaterial(
                matID: oldMat.MaterialID,
                uniq: oldMat.JobUniqueStr,
                proc: 1,
                part: partName,
                numProc: 1));

            _trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
               "During unload, found " + state.PartCompletedMessages.Count.ToString() + " parts that were unloaded/completed");

            //allocate new materials, one per completed part in addition to the existing one
            //Seems that multiple part completed messages are not multiple completed parts?
            //for (int i = 1; i < state.PartCompletedMessages.Count; i++) {
            //    var newId = _log.AllocateMaterialID(oldMat.JobUniqueStr);
            //    ret.Add(new LogMaterial(
            //       matID: newId,
            //       uniq: oldMat.JobUniqueStr,
            //       proc: 1,
            //       part: partName,
            //       numProc: 1));
            //}

            return ret;
        }


        #endregion

    }
}