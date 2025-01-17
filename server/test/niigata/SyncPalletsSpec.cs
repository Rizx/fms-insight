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
using Xunit;
using BlackMaple.MachineFramework;
using FluentAssertions;
using NSubstitute;
using System.Collections.Immutable;
using Germinate;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class SyncPalletsSpec : IDisposable
  {
    private FMSSettings _settings;
    private RepositoryConfig _logDBCfg;
    private IRepository _logDB;
    private IccSimulator _sim;
    private SyncPallets _sync;
    private Xunit.Abstractions.ITestOutputHelper _output;
    private bool _debugLogEnabled = false;
    private Action<CurrentStatus> _onCurrentStatus;
    private Newtonsoft.Json.JsonSerializerSettings jsonSettings;

    public SyncPalletsSpec(Xunit.Abstractions.ITestOutputHelper o)
    {
      _output = o;
      _settings = new FMSSettings()
      {
        SerialType = SerialType.AssignOneSerialPerMaterial,
        ConvertMaterialIDToSerial = FMSSettings.ConvertToBase62,
        ConvertSerialToMaterialID = FMSSettings.ConvertFromBase62,
      };
      _settings.Queues.Add("Transfer", new QueueSize() { MaxSizeBeforeStopUnloading = -1 });
      _settings.Queues.Add("sizedQ", new QueueSize() { MaxSizeBeforeStopUnloading = 1 });

      _logDBCfg = RepositoryConfig.InitializeSingleThreadedMemoryDB(new FMSSettings());
      _logDB = _logDBCfg.OpenConnection();

      jsonSettings = new Newtonsoft.Json.JsonSerializerSettings();
      jsonSettings.Converters.Add(new BlackMaple.MachineFramework.TimespanConverter());
      jsonSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
      jsonSettings.DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Utc;
      jsonSettings.Formatting = Newtonsoft.Json.Formatting.Indented;
    }

    private void InitSim(NiigataStationNames statNames, int numPals = 10, int numMachines = 6, int numLoads = 2)
    {

      var machConn = NSubstitute.Substitute.For<ICncMachineConnection>();

      var assign = new MultiPalletAssign(new IAssignPallets[] {
  new AssignNewRoutesOnPallets(statNames),
  new SizedQueues(_settings.Queues)
      });
      var createLog = new CreateCellState(_settings, statNames, machConn);

      _sim = new IccSimulator(numPals: numPals, numMachines: numMachines, numLoads: numLoads, statNames: statNames);
      var decr = new DecrementNotYetStartedJobs();

      _onCurrentStatus = Substitute.For<Action<CurrentStatus>>();
      _sync = new SyncPallets(_logDBCfg, _sim, assign, createLog, decr, _settings, onNewCurrentStatus: _onCurrentStatus);

      _sim.OnNewProgram += (newprog) =>
        _logDB.SetCellControllerProgramForProgram(newprog.ProgramName, newprog.ProgramRevision, newprog.ProgramNum.ToString());
    }

    public void Dispose()
    {
      _sync.Dispose();
      _logDBCfg.CloseMemoryConnection();
    }

    private IEnumerable<LogEntry> Step()
    {
      if (!_sim.Step())
      {
        return null;
      }
      using (var logMonitor = _logDBCfg.Monitor())
      {
        _sync.SynchronizePallets(false);
        var evts = logMonitor.OccurredEvents.Where(e => e.EventName == "NewLogEntry").Select(e => e.Parameters[0]).Cast<LogEntry>();
        if (evts.Any(e => e.LogType != LogType.PalletInStocker && e.LogType != LogType.PalletOnRotaryInbound))
        {
          _onCurrentStatus.ReceivedCalls().Should().NotBeEmpty();
        }
        _onCurrentStatus.ClearReceivedCalls();
        return evts;
      }
    }

    private IEnumerable<LogEntry> Run()
    {
      var logs = new List<LogEntry>();
      while (true)
      {
        var newLogs = Step();
        if (newLogs != null)
        {
          logs.AddRange(newLogs);
        }
        else
        {
          break;
        }
        if (_debugLogEnabled)
        {
          if (newLogs.Any())
          {
            _output.WriteLine("");
            _output.WriteLine("*************** Events *********************************");
            _output.WriteLine("Events: ");
            WriteLogs(newLogs);
          }
          _output.WriteLine("");
          _output.WriteLine("--------------- Status ---------------------------------");
          _output.WriteLine(_sim.DebugPrintStatus());
        }
      }
      return logs;
    }

    private void WriteLogs(IEnumerable<MachineFramework.LogEntry> es)
    {
      var output = new System.Text.StringBuilder();
      foreach (var e in es)
      {
        Action writeMat = () => output.AppendJoin(',', e.Material.Select(m => m.PartName + "-" + m.Process.ToString() + "[" + m.MaterialID.ToString() + "]"));
        switch (e.LogType)
        {
          case LogType.LoadUnloadCycle:
            if (e.StartOfCycle)
            {
              output.AppendFormat("{0}-Start on {1} at L/U{2} for ", e.Result, e.Pallet, e.LocationNum);
              writeMat();
              output.AppendLine();
            }
            else
            {
              output.AppendFormat("{0}-End on {1} at L/U{2} for ", e.Result, e.Pallet, e.LocationNum);
              writeMat();
              output.AppendLine();
            }
            break;

          case LogType.MachineCycle:
            if (e.StartOfCycle)
            {
              output.AppendFormat("Machine-Start on {0} at MC{1} of {2} for ", e.Pallet, e.LocationNum, e.Program);
              writeMat();
              output.AppendLine();
            }
            else
            {
              output.AppendFormat("Machine-End on {0} at MC{1} of {2} for ", e.Pallet, e.LocationNum, e.Program);
              writeMat();
              output.AppendLine();
            }
            break;

          case LogType.PartMark:
            output.AppendFormat("Assign {0} to ", e.Result);
            writeMat();
            output.AppendLine();
            break;

          case LogType.PalletCycle:
            output.AppendFormat("Pallet cycle for {0}", e.Pallet);
            output.AppendLine();
            break;
        }
      }
      _output.WriteLine(output.ToString());
    }

    private void AddJobs(IEnumerable<Job> jobs, IEnumerable<(string prog, long rev)> progs)
    {
      AddJobs(jobs.ToImmutableList(), progs);
    }

    private void AddJobs(ImmutableList<Job> jobs, IEnumerable<(string prog, long rev)> progs)
    {
      AddJobs(new NewJobs()
      {
        Jobs = jobs,
        Programs =
            progs.Select(p =>
            new MachineFramework.NewProgramContent()
            {
              ProgramName = p.prog,
              Revision = p.rev,
              Comment = "Comment " + p.prog + " rev" + p.rev.ToString(),
              ProgramContent = "ProgramCt " + p.prog + " rev" + p.rev.ToString()
            }).ToImmutableList()
      });
    }

    private void ExpectNewRoute()
    {
      using (var logMonitor = _logDBCfg.Monitor())
      {
        _sync.SynchronizePallets(false);
        var evts = logMonitor.OccurredEvents.Where(e => e.EventName == "NewLogEntry").Select(e => e.Parameters[0]).Cast<LogEntry>();
        evts.Count(e => e.Result == "New Niigata Route").Should().BePositive();
      }
    }

    private void AddJobs(NewJobs jobs, bool expectNewRoute = true)
    {
      _logDB.AddJobs(jobs, null, addAsCopiedToSystem: true);
      using (var logMonitor = _logDBCfg.Monitor())
      {
        _sync.SynchronizePallets(false);
        if (expectNewRoute)
        {
          var evts = logMonitor.OccurredEvents.Where(e => e.EventName == "NewLogEntry").Select(e => e.Parameters[0]).Cast<LogEntry>();
          evts.Count(e => e.Result == "New Niigata Route").Should().BePositive();
        }
      }
    }

    private void CheckSingleMaterial(IEnumerable<LogEntry> logs,
        long matId, string uniq, string part, int numProc, int[][] pals, string queue = null, bool[] reclamp = null,
        string[][] machGroups = null, string castingQueue = null, (string prog, long rev)[][] progs = null
    )
    {
      var matLogs = logs.Where(e =>
        e.LogType != LogType.PalletInStocker && e.LogType != LogType.PalletOnRotaryInbound
      ).ToList();
      matLogs.Should().BeInAscendingOrder(e => e.EndTimeUTC);

      machGroups = machGroups ?? Enumerable.Range(1, numProc).Select(_ => new string[] { "MC" }).ToArray();

      var expected = new List<Action<LogEntry>>();
      for (int procNum = 1; procNum <= numProc; procNum++)
      {
        int proc = procNum; // so lambdas capture constant proc
        if (proc == 1)
        {
          if (castingQueue != null)
          {
            expected.Add(e =>
            {
              e.LogType.Should().Be(LogType.RemoveFromQueue);
              e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
              e.LocationName.Should().Be(castingQueue);
            });
          }
          else
          {
            expected.Add(mark =>
            {
              mark.LogType.Should().Be(LogType.PartMark);
              mark.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
              mark.Result.Should().Be(_settings.ConvertMaterialIDToSerial(matId));
            });
          }
        }

        if (proc > 1 && queue != null)
        {
          expected.Add(e =>
          {
            e.LogType.Should().Be(LogType.RemoveFromQueue);
            e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
            e.LocationName.Should().Be(queue);
          });
        }

        expected.Add(e =>
        {
          e.Pallet.Should().BeOneOf(pals[proc - 1].Select(p => p.ToString()));
          e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
          e.LogType.Should().Be(LogType.LoadUnloadCycle);
          e.StartOfCycle.Should().BeFalse();
          e.Result.Should().Be("LOAD");
        });

        foreach (var (group, grpIdx) in machGroups[proc - 1].Select((g, idx) => (g, idx)))
        {
          expected.Add(e =>
          {
            e.Pallet.Should().BeOneOf(pals[proc - 1].Select(p => p.ToString()));
            e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
            e.LogType.Should().Be(LogType.MachineCycle);
            e.LocationName.Should().Be(group);
            e.StartOfCycle.Should().BeTrue();

            if (progs != null && proc - 1 < progs.Length && grpIdx < progs[proc - 1].Length)
            {
              var p = progs[proc - 1][grpIdx];
              e.Program.Should().BeEquivalentTo(p.prog);
              e.ProgramDetails["ProgramRevision"].Should().BeEquivalentTo(p.rev.ToString());
            }
          });
          expected.Add(e =>
          {
            e.Pallet.Should().BeOneOf(pals[proc - 1].Select(p => p.ToString()));
            e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
            e.LogType.Should().Be(LogType.MachineCycle);
            e.LocationName.Should().Be(group);
            e.StartOfCycle.Should().BeFalse();

            if (progs != null && proc - 1 < progs.Length && grpIdx < progs[proc - 1].Length)
            {
              var p = progs[proc - 1][grpIdx];
              e.Program.Should().BeEquivalentTo(p.prog);
              e.ProgramDetails["ProgramRevision"].Should().BeEquivalentTo(p.rev.ToString());
            }
          });
        }

        if (reclamp != null && reclamp[proc - 1])
        {
          expected.Add(e =>
          {
            e.Pallet.Should().BeOneOf(pals[proc - 1].Select(p => p.ToString()));
            e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
            e.LogType.Should().Be(LogType.LoadUnloadCycle);
            e.Program.Should().Be("TestReclamp");
            e.Result.Should().Be("TestReclamp");
            e.StartOfCycle.Should().BeTrue();
          });
          expected.Add(e =>
          {
            e.Pallet.Should().BeOneOf(pals[proc - 1].Select(p => p.ToString()));
            e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
            e.LogType.Should().Be(LogType.LoadUnloadCycle);
            e.Program.Should().Be("TestReclamp");
            e.Result.Should().Be("TestReclamp");
            e.StartOfCycle.Should().BeFalse();
          });
        }

        if (proc < numProc && queue != null)
        {
          expected.Add(e =>
          {
            e.LogType.Should().Be(LogType.AddToQueue);
            e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
            e.LocationName.Should().Be(queue);
          });
        }

        expected.Add(e =>
        {
          e.Pallet.Should().BeOneOf(pals[proc - 1].Select(p => p.ToString()));
          e.LogType.Should().Be(LogType.LoadUnloadCycle);
          e.StartOfCycle.Should().BeFalse();
          e.Result.Should().Be("UNLOAD");
        });
      }

      //_output.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(matLogs, jsonSettings));

      matLogs.Should().SatisfyRespectively(expected);
    }

    private void CheckMaxQueueSize(IEnumerable<LogEntry> logs, string queue, int expectedMax)
    {
      int cnt = 0;
      int max = 0;

      foreach (var e in logs)
      {
        if (e.LogType == LogType.AddToQueue && e.LocationName == queue)
        {
          cnt += 1;
          max = Math.Max(cnt, max);
        }
        else if (e.LogType == LogType.RemoveFromQueue && e.LocationName == queue)
        {
          cnt -= 1;
        }
      }

      cnt.Should().Be(0);
      max.Should().Be(expectedMax);
    }

    [Fact]
    public void OneProcJob()
    {
      InitSim(new NiigataStationNames()
      {
        ReclampGroupNames = new HashSet<string>() { "TestReclamp" },
        IccMachineToJobMachNames = Enumerable.Range(1, 6).ToDictionary(mc => mc, mc => (group: "TestMC", num: mc + 100))
      });

      var j = FakeIccDsl.CreateOneProcOnePathJob(
        unique: "uniq1",
        part: "part1",
        qty: 3,
        priority: 1,
        partsPerPal: 1,
        pals: new[] { 1, 2 },
        fixture: "fix1",
        face: 1,
        luls: new[] { 1 },
        loadMins: 8,
        machs: new[] { 5, 6 },
        machMins: 14,
        prog: "prog111",
        progRev: null,
        unloadMins: 5
      );

      AddJobs(new[] { j }, new[] { (prog: "prog111", rev: 5L) });

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);

      byMat.Count.Should().Be(3);
      foreach (var m in byMat)
      {
        CheckSingleMaterial(m, m.Key, "uniq1", "part1", 1, pals: new[] { new[] { 1, 2 } }, machGroups: new[] { new[] { "TestMC" } });
      }
    }

    [Fact]
    public void MultpleProcsMultiplePathsSeparatePallets()
    {
      InitSim(new NiigataStationNames()
      {
        ReclampGroupNames = new HashSet<string>() { "TestReclamp" },
        IccMachineToJobMachNames = Enumerable.Range(1, 6).ToDictionary(mc => mc, mc => (group: "MC", num: mc))
      });

      var j = new Job()
      {
        UniqueStr = "uniq1",
        PartName = "part1",
        Cycles = 7,
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              new ProcPathInfo()
              {
                Load = ImmutableList.Create(1),
                Unload = ImmutableList.Create(1),
                ExpectedLoadTime = TimeSpan.FromMinutes(8),
                ExpectedUnloadTime = TimeSpan.FromMinutes(5),
                PartsPerPallet = 1,
                Pallets = ImmutableList.Create("1", "2"),
                Fixture = "fix1",
                Face = 1,
                OutputQueue = "transQ",
                Stops = ImmutableList.Create(new MachiningStop()
                {
                  StationGroup = "MC",
                  Stations = ImmutableList.Create(5, 6),
                  ExpectedCycleTime = TimeSpan.FromMinutes(14),
                  Program = "1111"
                })
              },
              new ProcPathInfo()
              {
                Load = ImmutableList.Create(2),
                Unload = ImmutableList.Create(2),
                ExpectedLoadTime = TimeSpan.FromMinutes(7),
                ExpectedUnloadTime = TimeSpan.FromMinutes(4),
                PartsPerPallet = 1,
                Pallets = ImmutableList.Create("5", "6"),
                Fixture = "fix1",
                Face = 1,
                OutputQueue = "transQ",
                Stops = ImmutableList.Create(new MachiningStop()
                {
                  StationGroup = "MC",
                  Stations = ImmutableList.Create(3, 4),
                  ExpectedCycleTime = TimeSpan.FromMinutes(12),
                  Program = "3333"
                })
              }
              )
          },
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              new ProcPathInfo()
              {
                Load = ImmutableList.Create(1),
                Unload = ImmutableList.Create(1),
                ExpectedLoadTime = TimeSpan.FromMinutes(3),
                ExpectedUnloadTime = TimeSpan.FromMinutes(6),
                PartsPerPallet = 1,
                Pallets = ImmutableList.Create("3", "4"),
                Fixture = "fix2",
                Face = 1,
                InputQueue = "transQ",
                Stops = ImmutableList.Create(new MachiningStop()
                {
                  StationGroup = "MC",
                  Stations = ImmutableList.Create(1, 2),
                  ExpectedCycleTime = TimeSpan.FromMinutes(19),
                  Program = "2222"
                })
              },
              new ProcPathInfo()
              {
                Load = ImmutableList.Create(2),
                Unload = ImmutableList.Create(2),
                ExpectedLoadTime = TimeSpan.FromMinutes(3),
                ExpectedUnloadTime = TimeSpan.FromMinutes(3),
                PartsPerPallet = 1,
                Pallets = ImmutableList.Create("7", "8"),
                Fixture = "fix2",
                Face = 1,
                InputQueue = "transQ",
                Stops = ImmutableList.Create(new MachiningStop()
                {
                  StationGroup = "MC",
                  Stations = ImmutableList.Create(1, 2),
                  ExpectedCycleTime = TimeSpan.FromMinutes(16),
                  Program = "4444"
                })
              }
              )
          })
      };

      AddJobs(new[] { j }, Enumerable.Empty<(string prog, long rev)>());

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);
      byMat.Count.Should().Be(7);

      var byPath = byMat.ToLookup(mat => (new[] { "1", "2" }).Contains(mat.Skip(1).First().Pallet));
      byPath[true].Count().Should().BeGreaterThan(0); // true is pallets 1 or 2, path 1
      byPath[false].Count().Should().BeGreaterThan(0); // false is pallets 5 or 6, path 2

      foreach (var path in byPath)
      {
        foreach (var m in path)
        {
          CheckSingleMaterial(
            logs: m,
            matId: m.Key,
            uniq: "uniq1",
            part: "part1",
            numProc: 2,
            queue: "transQ",
            pals:
              path.Key
                ? new[] { new[] { 1, 2 }, new[] { 3, 4, 7, 8 } }
                : new[] { new[] { 5, 6 }, new[] { 3, 4, 7, 8 } }
          );
        }
      }

    }

    [Fact]
    public void MultipleProcsMultiplePathsSamePallet()
    {
      InitSim(new NiigataStationNames()
      {
        ReclampGroupNames = new HashSet<string>() { "TestReclamp" },
        IccMachineToJobMachNames = Enumerable.Range(1, 6).ToDictionary(mc => mc, mc => (group: "MC", num: mc))
      });


      var j = new Job()
      {
        UniqueStr = "uniq1",
        PartName = "part1",
        Cycles = 14,
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              new ProcPathInfo()
              {
                Load = ImmutableList.Create(1),
                Unload = ImmutableList.Create(1),
                ExpectedLoadTime = TimeSpan.FromMinutes(8),
                ExpectedUnloadTime = TimeSpan.FromMinutes(5),
                PartsPerPallet = 2,
                Pallets = ImmutableList.Create("1", "2"),
                Fixture = "fix1",
                Face = 1,
                Stops = ImmutableList.Create(new MachiningStop()
                {
                  StationGroup = "MC",
                  Stations = ImmutableList.Create(5, 6),
                  ExpectedCycleTime = TimeSpan.FromMinutes(14),
                  Program = "1111"
                })
              },
              new ProcPathInfo()
              {
                Load = ImmutableList.Create(2),
                Unload = ImmutableList.Create(2),
                ExpectedLoadTime = TimeSpan.FromMinutes(7),
                ExpectedUnloadTime = TimeSpan.FromMinutes(4),
                PartsPerPallet = 2,
                Pallets = ImmutableList.Create("3", "4"),
                Fixture = "fix1",
                Face = 1,
                Stops = ImmutableList.Create(new MachiningStop()
                {
                  StationGroup = "MC",
                  Stations = ImmutableList.Create(5, 6),
                  ExpectedCycleTime = TimeSpan.FromMinutes(12),
                  Program = "3333"
                })
              }
              )
          },
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              new ProcPathInfo()
              {
                Load = ImmutableList.Create(1),
                Unload = ImmutableList.Create(1),
                ExpectedLoadTime = TimeSpan.FromMinutes(3),
                ExpectedUnloadTime = TimeSpan.FromMinutes(6),
                PartsPerPallet = 2,
                Pallets = ImmutableList.Create("1", "2"),
                Fixture = "fix1",
                Face = 2,
                Stops = ImmutableList.Create(new MachiningStop()
                {
                  StationGroup = "MC",
                  Stations = ImmutableList.Create(5, 6),
                  ExpectedCycleTime = TimeSpan.FromMinutes(19),
                  Program = "2222"
                })
              },
              new ProcPathInfo()
              {
                Load = ImmutableList.Create(2),
                Unload = ImmutableList.Create(2),
                ExpectedLoadTime = TimeSpan.FromMinutes(3),
                ExpectedUnloadTime = TimeSpan.FromMinutes(3),
                PartsPerPallet = 2,
                Pallets = ImmutableList.Create("3", "4"),
                Fixture = "fix1",
                Face = 2,
                Stops = ImmutableList.Create(new MachiningStop()
                {
                  StationGroup = "MC",
                  Stations = ImmutableList.Create(5, 6),
                  ExpectedCycleTime = TimeSpan.FromMinutes(16),
                  Program = "4444"
                })
              }
              )
          })
      };

      AddJobs(new[] { j }, Enumerable.Empty<(string prog, long rev)>());

      var logs = Run();

      var matIds = logs.SelectMany(e => e.Material).Select(m => m.MaterialID).ToHashSet();
      matIds.Count.Should().Be(14);

      var byPath =
        matIds
          .Select(matId => new { matId, logs = logs.Where(e => e.Material.Any(m => m.MaterialID == matId)) })
          .ToLookup(mat => (new[] { "1", "2" }).Contains(mat.logs.Skip(1).First().Pallet));
      byPath[true].Count().Should().BeGreaterThan(0); // true is pallets 1 or 2, path 1
      byPath[false].Count().Should().BeGreaterThan(0); // false is pallets 3 or 4, path 2

      foreach (var path in byPath)
      {
        foreach (var m in path)
        {
          CheckSingleMaterial(
            logs: m.logs,
            matId: m.matId,
            uniq: "uniq1",
            part: "part1",
            numProc: 2,
            pals:
              path.Key
                ? new[] { new[] { 1, 2 }, new[] { 1, 2 } }
                : new[] { new[] { 3, 4 }, new[] { 3, 4 } }
          );
        }
      }


    }

    [Fact(Skip = "Holding at machine not yet supported by Niigata")]
    public void SizedQueue()
    {
      InitSim(new NiigataStationNames()
      {
        ReclampGroupNames = new HashSet<string>() { "TestReclamp" },
        IccMachineToJobMachNames = Enumerable.Range(1, 6).ToDictionary(mc => mc, mc => (group: "MC", num: mc))
      });

      var j = new Job()
      {
        UniqueStr = "uniq1",
        PartName = "part1",
        Cycles = 8,
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              // process 1 on 4 pallets with short times
              new ProcPathInfo()
              {
                Load = ImmutableList.Create(1),
                Unload = ImmutableList.Create(1),
                ExpectedLoadTime = TimeSpan.FromMinutes(2),
                ExpectedUnloadTime = TimeSpan.FromMinutes(3),
                PartsPerPallet = 1,
                Pallets = ImmutableList.Create("1", "2", "3", "4"),
                Fixture = "fix1",
                Face = 1,
                OutputQueue = "sizedQ",
                Stops = ImmutableList.Create(new MachiningStop()
                {
                  StationGroup = "MC",
                  Stations = ImmutableList.Create(1, 2, 3, 4, 5, 6),
                  ExpectedCycleTime = TimeSpan.FromMinutes(5),
                  Program = "1111"
                })
              }
              )
          },
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              //process 2 on only 2 pallets with longer times
              new ProcPathInfo()
              {
                Load = ImmutableList.Create(1),
                Unload = ImmutableList.Create(1),
                ExpectedLoadTime = TimeSpan.FromMinutes(3),
                ExpectedUnloadTime = TimeSpan.FromMinutes(4),
                PartsPerPallet = 1,
                Pallets = ImmutableList.Create("5", "6"),
                Fixture = "fix2",
                Face = 1,
                InputQueue = "sizedQ",
                Stops = ImmutableList.Create(new MachiningStop()
                {
                  StationGroup = "MC",
                  Stations = ImmutableList.Create(1, 2, 3, 4, 5, 6),
                  ExpectedCycleTime = TimeSpan.FromMinutes(20),
                  Program = "2222"
                })
              }
              )
          })
      };

      AddJobs(new[] { j }, Enumerable.Empty<(string prog, long rev)>());

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);

      byMat.Count.Should().Be(8);
      foreach (var m in byMat)
      {
        CheckSingleMaterial(m, m.Key, "uniq1", "part1", 2, pals: new[] { new[] { 1, 2, 3, 4 }, new[] { 5, 6 } }, queue: "sizedQ");
      }

      CheckMaxQueueSize(logs, "sizedQ", 1);
    }

    [Fact]
    public void SizedQueueWithReclamp()
    {
      InitSim(new NiigataStationNames()
      {
        ReclampGroupNames = new HashSet<string>() { "TestReclamp" },
        IccMachineToJobMachNames = Enumerable.Range(1, 6).ToDictionary(mc => mc, mc => (group: "MC", num: mc))
      });

      var j = new Job()
      {
        UniqueStr = "uniq1",
        PartName = "part1",
        Cycles = 4,
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              // process 1 on 4 pallets with short times
              new ProcPathInfo()
              {
                Load = ImmutableList.Create(1),
                Unload = ImmutableList.Create(1),
                ExpectedLoadTime = TimeSpan.FromMinutes(2),
                ExpectedUnloadTime = TimeSpan.FromMinutes(3),
                PartsPerPallet = 1,
                Pallets = ImmutableList.Create("1", "2", "3", "4"),
                Fixture = "fix1",
                Face = 1,
                OutputQueue = "sizedQ",
                Stops = ImmutableList.Create(
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = ImmutableList.Create(1, 2, 3, 4, 5, 6),
                    ExpectedCycleTime = TimeSpan.FromMinutes(5),
                    Program = "1111"
                  },
                  new MachiningStop()
                  {
                    StationGroup = "TestReclamp",
                    Stations = ImmutableList.Create(2),
                    ExpectedCycleTime = TimeSpan.FromMinutes(3),
                  }
                )
              }
              )
          },
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              //process 2 on only 2 pallets with longer times
              new ProcPathInfo()
              {
                Load = ImmutableList.Create(1),
                Unload = ImmutableList.Create(1),
                ExpectedLoadTime = TimeSpan.FromMinutes(3),
                ExpectedUnloadTime = TimeSpan.FromMinutes(4),
                PartsPerPallet = 1,
                Pallets = ImmutableList.Create("5", "6"),
                Fixture = "fix2",
                Face = 1,
                InputQueue = "sizedQ",
                Stops = ImmutableList.Create(new MachiningStop()
                {
                  StationGroup = "MC",
                  Stations = ImmutableList.Create(1, 2, 3, 4, 5, 6),
                  ExpectedCycleTime = TimeSpan.FromMinutes(20),
                  Program = "2222"
                })
              }
              )
          })
      };

      AddJobs(new[] { j }, Enumerable.Empty<(string prog, long rev)>());

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);

      byMat.Count.Should().Be(4);
      foreach (var m in byMat)
      {
        CheckSingleMaterial(m, m.Key, "uniq1", "part1", 2,
          pals: new[] { new[] { 1, 2, 3, 4 }, new[] { 5, 6 } },
          queue: "sizedQ",
          reclamp: new[] { true, false });
      }

      CheckMaxQueueSize(logs, "sizedQ", 1);
    }

    [Fact]
    public void TwoMachineStops()
    {
      InitSim(new NiigataStationNames()
      {
        ReclampGroupNames = new HashSet<string>() { },
        IccMachineToJobMachNames = new Dictionary<int, (string group, int num)> {
    {1, (group: "RO", num: 1)},
    {2, (group: "RO", num: 2)},
    {3, (group: "RO", num: 3)},
    {4, (group: "RO", num: 4)},
    {5, (group: "FC", num: 1)},
    {6, (group: "FC", num: 2)},
    {7, (group: "FC", num: 3)},
    {8, (group: "FC", num: 4)},
  }
      },
      numPals: 16,
      numLoads: 4,
      numMachines: 8);

      AddJobs(Newtonsoft.Json.JsonConvert.DeserializeObject<NewJobs>(
        System.IO.File.ReadAllText("../../../sample-newjobs/two-stops.json"),
        jsonSettings
      ));

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);

      byMat.Count.Should().Be(9);
      foreach (var m in byMat)
      {
        CheckSingleMaterial(
          m,
          matId: m.Key,
          uniq: "aaa-0NovSzGF9VZGS00b_",
          part: "aaa",
          numProc: 4,
          queue: "Transfer",
          pals: new[] {
      new[] { 2, 3, 4 },
      new[] { 5, 6, 7, 8, 9, 10 },
      new[] { 12, 13, 14, 15, 16},
      new[] { 11 }
          },
          machGroups: new[] {
      new[] { "RO"},
      new[] { "RO", "FC"},
      new[] { "FC"},
      new[] {"RO"}
          },
          progs: new[] {
      new[] { (prog: "aaa1RO", rev: 1L) },
      new[] { (prog: "aaa2RO", rev: 1L), (prog: "aaa2FC", rev: 1L) },
      new[] { (prog: "aaa3FC", rev: 1L) },
      new[] { (prog: "aaa4RO", rev: 1L) },
          }
        );
      }
    }

    [Fact]
    public void PerMaterialWorkorderPrograms()
    {
      InitSim(new NiigataStationNames()
      {
        ReclampGroupNames = new HashSet<string>() { },
        IccMachineToJobMachNames = new Dictionary<int, (string group, int num)> {
    {1, (group: "RO", num: 1)},
    {2, (group: "RO", num: 2)},
    {3, (group: "RO", num: 3)},
    {4, (group: "RO", num: 4)},
    {5, (group: "FC", num: 1)},
    {6, (group: "FC", num: 2)},
    {7, (group: "FC", num: 3)},
    {8, (group: "FC", num: 4)},
  }
      },
      numPals: 16,
      numLoads: 4,
      numMachines: 8);

      var newJobs = Newtonsoft.Json.JsonConvert.DeserializeObject<NewJobs>(
        System.IO.File.ReadAllText("../../../sample-newjobs/two-stops.json"),
        jsonSettings
      );
      newJobs %= j =>
      {
        for (int jobIdx = 0; jobIdx < j.Jobs.Count; jobIdx++)
        {
          j.Jobs[jobIdx] %= draftJob => draftJob.AdjustAllPaths((proc, path, draftPath) =>
          {
            if (proc == 1)
            {
              draftPath.InputQueue = "castingQ";
            }
            draftPath.Stops.AdjustAll(d =>
      {
        d.Program = null;
        d.ProgramRevision = null;
      });
          });
        }
        j.CurrentUnfilledWorkorders.AddRange(new[] {
    new Workorder()
    {
      WorkorderId = "work1",
      Part = "aaa",
      Programs = ImmutableList.Create(
      new ProgramForJobStep() { ProcessNumber = 1, StopIndex = 0, ProgramName = "aaa1RO", Revision = -1 },
      new ProgramForJobStep() { ProcessNumber = 2, StopIndex = 0, ProgramName = "aaa2RO", Revision = -1 },
      new ProgramForJobStep() { ProcessNumber = 2, StopIndex = 1, ProgramName = "aaa2FC", Revision = -1 },
      new ProgramForJobStep() { ProcessNumber = 3, ProgramName = "aaa3FC", Revision = -1 },
      new ProgramForJobStep() { ProcessNumber = 4, ProgramName = "aaa4RO", Revision = -1 }
    )
    },
    new Workorder()
    {
      WorkorderId = "work2",
      Part = "aaa",
      Programs = ImmutableList.Create(
      new ProgramForJobStep() { ProcessNumber = 1, StopIndex = 0, ProgramName = "aaa1RO", Revision = -2 },
      new ProgramForJobStep() { ProcessNumber = 2, StopIndex = 0, ProgramName = "aaa2RO", Revision = -2 },
      new ProgramForJobStep() { ProcessNumber = 2, StopIndex = 1, ProgramName = "aaa2FC", Revision = -2 },
      new ProgramForJobStep() { ProcessNumber = 3, ProgramName = "zzz3FC", Revision = -1 },
      new ProgramForJobStep() { ProcessNumber = 4, ProgramName = "zzz4RO", Revision = -1 }
    )
    }
        });
        j.Programs.AddRange(new[] {
    new MachineFramework.NewProgramContent() { ProgramName = "aaa1RO", Revision = -2, Comment = "a 1 RO rev -2", ProgramContent = "aa 1 RO rev-2"},
    new MachineFramework.NewProgramContent() { ProgramName = "aaa2RO", Revision = -2, Comment = "a 2 RO rev -2", ProgramContent = "aa 2 RO rev-2"},
    new MachineFramework.NewProgramContent() { ProgramName = "aaa2FC", Revision = -2, Comment = "a 2 FC rev -2", ProgramContent = "aa 2 FC rev-2"},
    new MachineFramework.NewProgramContent() { ProgramName = "zzz3FC", Revision = -1, Comment = "z 3 RO rev -1", ProgramContent = "zz 3 FC rev-1"},
    new MachineFramework.NewProgramContent() { ProgramName = "zzz4RO", Revision = -1, Comment = "z 4 RO rev -1", ProgramContent = "zz 4 RO rev-1"},
        });
      };

      AddJobs(newJobs, expectNewRoute: false); // no route yet because no material

      // plan quantity of 9, but add 10 parts
      var addTime = DateTime.UtcNow;
      for (int i = 0; i < 10; i++)
      {
        var m = _logDB.AllocateMaterialIDForCasting("aaa");
        _logDB.RecordWorkorderForMaterialID(m, 0, i % 2 == 0 ? "work1" : "work2");
        _logDB.RecordAddMaterialToQueue(new EventLogMaterial() { MaterialID = m, Process = 0, Face = "" }, "castingQ", -1, "theoperator", "testsuite", addTime);
      }

      ExpectNewRoute();

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);

      byMat.Count.Should().Be(9);
      foreach (var m in byMat)
      {
        CheckSingleMaterial(
          m,
          matId: m.Key,
          uniq: "aaa-0NovSzGF9VZGS00b_",
          part: "aaa",
          numProc: 4,
          castingQueue: "castingQ",
          queue: "Transfer",
          pals: new[] {
      new[] { 2, 3, 4 },
      new[] { 5, 6, 7, 8, 9, 10 },
      new[] { 12, 13, 14, 15, 16},
      new[] { 11 }
          },
          machGroups: new[] {
      new[] { "RO"},
      new[] { "RO", "FC"},
      new[] { "FC"},
      new[] {"RO"}
          },
          progs: m.Key % 2 == 1 // material ids start at 1, so odd is first
            ? new[] {
    new[] { (prog: "aaa1RO", rev: 1L) },
    new[] { (prog: "aaa2RO", rev: 1L), (prog: "aaa2FC", rev: 1L) },
    new[] { (prog: "aaa3FC", rev: 1L) },
    new[] { (prog: "aaa4RO", rev: 1L) },
              }
            : new[] {
    new[] { (prog: "aaa1RO", rev: 2L) },
    new[] { (prog: "aaa2RO", rev: 2L), (prog: "aaa2FC", rev: 2L) },
    new[] { (prog: "zzz3FC", rev: 1L) },
    new[] { (prog: "zzz4RO", rev: 1L) },
              }
        );
      }

      _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
  new QueuedMaterial() {
    MaterialID = 10,
    Queue = "castingQ",
    Position = 0,
    Unique = "",
    PartNameOrCasting = "aaa",
    NumProcesses = 1,
    AddTimeUTC = addTime
  }
      });
    }

  }
}