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
using System.Data;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework
{
  public partial class Repository
  {
    #region Loading
    private List<MachineWatchInterface.LogEntry> LoadLog(IDataReader reader, IDbTransaction trans = null)
    {
      using (var matCmd = _connection.CreateCommand())
      using (var detailCmd = _connection.CreateCommand())
      using (var toolCmd = _connection.CreateCommand())
      {
        if (trans != null)
        {
          ((IDbCommand)matCmd).Transaction = trans;
          ((IDbCommand)detailCmd).Transaction = trans;
          ((IDbCommand)toolCmd).Transaction = trans;
        }
        matCmd.CommandText = "SELECT stations_mat.MaterialID, UniqueStr, Process, PartName, NumProcesses, Face, Serial, Workorder " +
          " FROM stations_mat " +
          " LEFT OUTER JOIN matdetails ON stations_mat.MaterialID = matdetails.MaterialID " +
          " WHERE stations_mat.Counter = $cntr " +
          " ORDER BY stations_mat.Counter ASC";
        matCmd.Parameters.Add("cntr", SqliteType.Integer);

        detailCmd.CommandText = "SELECT Key, Value FROM program_details WHERE Counter = $cntr";
        detailCmd.Parameters.Add("cntr", SqliteType.Integer);

        toolCmd.CommandText = "SELECT Tool, UseInCycle, UseAtEndOfCycle, ToolLife, ToolChange FROM station_tools WHERE Counter = $cntr";
        toolCmd.Parameters.Add("cntr", SqliteType.Integer);

        var lst = new List<MachineWatchInterface.LogEntry>();

        while (reader.Read())
        {
          long ctr = reader.GetInt64(0);
          string pal = reader.GetString(1);
          int logType = reader.GetInt32(2);
          int locNum = reader.GetInt32(3);
          string prog = reader.GetString(4);
          bool start = reader.GetBoolean(5);
          System.DateTime timeUTC = new DateTime(reader.GetInt64(6), DateTimeKind.Utc);
          string result = reader.GetString(7);
          bool endOfRoute = false;
          if (!reader.IsDBNull(8))
            endOfRoute = reader.GetBoolean(8);
          TimeSpan elapsed = TimeSpan.FromMinutes(-1);
          if (!reader.IsDBNull(9))
            elapsed = TimeSpan.FromTicks(reader.GetInt64(9));
          TimeSpan active = TimeSpan.Zero;
          if (!reader.IsDBNull(10))
            active = TimeSpan.FromTicks(reader.GetInt64(10));
          string locName = null;
          if (!reader.IsDBNull(11))
            locName = reader.GetString(11);

          MachineWatchInterface.LogType ty;
          if (Enum.IsDefined(typeof(MachineWatchInterface.LogType), logType))
          {
            ty = (MachineWatchInterface.LogType)logType;
            if (locName == null)
            {
              //For compatibility with old logs
              switch (ty)
              {
                case MachineWatchInterface.LogType.GeneralMessage:
                  locName = "General";
                  break;
                case MachineWatchInterface.LogType.Inspection:
                  locName = "Inspect";
                  break;
                case MachineWatchInterface.LogType.LoadUnloadCycle:
                  locName = "Load";
                  break;
                case MachineWatchInterface.LogType.MachineCycle:
                  locName = "MC";
                  break;
                case MachineWatchInterface.LogType.OrderAssignment:
                  locName = "Order";
                  break;
                case MachineWatchInterface.LogType.PartMark:
                  locName = "Mark";
                  break;
                case MachineWatchInterface.LogType.PalletCycle:
                  locName = "Pallet Cycle";
                  break;
              }
            }
          }
          else
          {
            ty = MachineWatchInterface.LogType.GeneralMessage;
            switch (logType)
            {
              case 3: locName = "Machine"; break;
              case 4: locName = "Buffer"; break;
              case 5: locName = "Cart"; break;
              case 8: locName = "Wash"; break;
              case 9: locName = "Deburr"; break;
              default: locName = "Unknown"; break;
            }
          }

          var matLst = new List<MachineWatchInterface.LogMaterial>();
          matCmd.Parameters[0].Value = ctr;
          using (var matReader = matCmd.ExecuteReader())
          {
            while (matReader.Read())
            {
              string uniq = "";
              string part = "";
              int numProc = -1;
              string face = "";
              string serial = "";
              string workorder = "";
              if (!matReader.IsDBNull(1))
                uniq = matReader.GetString(1);
              if (!matReader.IsDBNull(3))
                part = matReader.GetString(3);
              if (!matReader.IsDBNull(4))
                numProc = matReader.GetInt32(4);
              if (!matReader.IsDBNull(5))
                face = matReader.GetString(5);
              if (!matReader.IsDBNull(6))
                serial = matReader.GetString(6);
              if (!matReader.IsDBNull(7))
                workorder = matReader.GetString(7);
              matLst.Add(new MachineWatchInterface.LogMaterial(
                  matID: matReader.GetInt64(0),
                                                                uniq: uniq,
                                                                proc: matReader.GetInt32(2),
                                                                part: part, numProc: numProc, serial: serial, workorder: workorder, face: face));
            }
          }

          var logRow = new MachineWatchInterface.LogEntry(ctr, matLst, pal,
                ty, locName, locNum,
              prog, start, timeUTC, result, endOfRoute, elapsed, active);

          detailCmd.Parameters[0].Value = ctr;
          using (var detailReader = detailCmd.ExecuteReader())
          {
            while (detailReader.Read())
            {
              logRow.ProgramDetails[detailReader.GetString(0)] = detailReader.GetString(1);
            }
          }

          toolCmd.Parameters[0].Value = ctr;
          using (var toolReader = toolCmd.ExecuteReader())
          {
            while (toolReader.Read())
            {
              logRow.Tools[toolReader.GetString(0)] = new MachineWatchInterface.ToolUse()
              {
                ToolUseDuringCycle = TimeSpan.FromTicks(toolReader.GetInt64(1)),
                TotalToolUseAtEndOfCycle = TimeSpan.FromTicks(toolReader.GetInt64(2)),
                ConfiguredToolLife = TimeSpan.FromTicks(toolReader.GetInt64(3)),
                ToolChangeOccurred = toolReader.IsDBNull(4) ? null : toolReader.GetBoolean(4) ? (bool?)true : null,
              };
            }
          }

          lst.Add(logRow);
        }

        return lst;

      } // close usings
    }

    public List<MachineWatchInterface.LogEntry> GetLogEntries(System.DateTime startUTC, System.DateTime endUTC)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
               " FROM stations WHERE TimeUTC >= $start AND TimeUTC <= $end ORDER BY Counter ASC";

          cmd.Parameters.Add("start", SqliteType.Integer).Value = startUTC.Ticks;
          cmd.Parameters.Add("end", SqliteType.Integer).Value = endUTC.Ticks;

          using (var reader = cmd.ExecuteReader())
          {
            return LoadLog(reader);
          }
        }
      }
    }

    public List<MachineWatchInterface.LogEntry> GetLog(long counter)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
               " FROM stations WHERE Counter > $cntr ORDER BY Counter ASC";
          cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;

          using (var reader = cmd.ExecuteReader())
          {
            return LoadLog(reader);
          }
        }
      }
    }

    public List<MachineWatchInterface.LogEntry> StationLogByForeignID(string foreignID)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
               " FROM stations WHERE ForeignID = $foreign ORDER BY Counter ASC";
          cmd.Parameters.Add("foreign", SqliteType.Text).Value = foreignID;

          using (var reader = cmd.ExecuteReader())
          {
            return LoadLog(reader);
          }
        }
      }
    }

    public string OriginalMessageByForeignID(string foreignID)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT OriginalMessage " +
               " FROM stations WHERE ForeignID = $foreign ORDER BY Counter DESC LIMIT 1";
          cmd.Parameters.Add("foreign", SqliteType.Text).Value = foreignID;

          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              if (reader.IsDBNull(0))
              {
                return "";
              }
              else
              {
                return reader.GetString(0);
              }
            }
          }
        }
      }
      return "";
    }

    public List<MachineWatchInterface.LogEntry> GetLogForMaterial(long materialID)
    {
      if (materialID < 0) return new List<MachineWatchInterface.LogEntry>();
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
               " FROM stations WHERE Counter IN (SELECT Counter FROM stations_mat WHERE MaterialID = $mat) ORDER BY Counter ASC";
          cmd.Parameters.Add("mat", SqliteType.Integer).Value = materialID;

          using (var reader = cmd.ExecuteReader())
          {
            return LoadLog(reader);
          }
        }
      }
    }

    public List<MachineWatchInterface.LogEntry> GetLogForMaterial(IEnumerable<long> materialIDs)
    {
      lock (_cfg)
      {
        var ret = new List<MachineWatchInterface.LogEntry>();
        using (var cmd = _connection.CreateCommand())
        using (var trans = _connection.BeginTransaction())
        {
          cmd.Transaction = trans;
          cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
               " FROM stations WHERE Counter IN (SELECT Counter FROM stations_mat WHERE MaterialID = $mat) ORDER BY Counter ASC";
          var param = cmd.Parameters.Add("mat", SqliteType.Integer);

          foreach (var matId in materialIDs)
          {
            param.Value = matId;
            using (var reader = cmd.ExecuteReader())
            {
              ret.AddRange(LoadLog(reader));
            }
          }
          trans.Commit();
        }
        return ret;
      }
    }

    public List<MachineWatchInterface.LogEntry> GetLogForSerial(string serial)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
              " FROM stations WHERE Counter IN (SELECT stations_mat.Counter FROM matdetails INNER JOIN stations_mat ON stations_mat.MaterialID = matdetails.MaterialID WHERE matdetails.Serial = $ser) ORDER BY Counter ASC";
          cmd.Parameters.Add("ser", SqliteType.Text).Value = serial;

          using (var reader = cmd.ExecuteReader())
          {
            return LoadLog(reader);
          }
        }
      }
    }

    public List<MachineWatchInterface.LogEntry> GetLogForJobUnique(string jobUnique)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
              " FROM stations WHERE Counter IN (SELECT stations_mat.Counter FROM matdetails INNER JOIN stations_mat ON stations_mat.MaterialID = matdetails.MaterialID WHERE matdetails.UniqueStr = $uniq) ORDER BY Counter ASC";
          cmd.Parameters.Add("uniq", SqliteType.Text).Value = jobUnique;

          using (var reader = cmd.ExecuteReader())
          {
            return LoadLog(reader);
          }
        }
      }
    }

    public List<MachineWatchInterface.LogEntry> GetLogForWorkorder(string workorder)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
              " FROM stations " +
              " WHERE Counter IN (SELECT stations_mat.Counter FROM matdetails INNER JOIN stations_mat ON stations_mat.MaterialID = matdetails.MaterialID WHERE matdetails.Workorder = $work) " +
              "    OR (Pallet = '' AND Result = $work AND StationLoc = $workloc) " +
              " ORDER BY Counter ASC";
          cmd.Parameters.Add("work", SqliteType.Text).Value = workorder;
          cmd.Parameters.Add("workloc", SqliteType.Integer).Value = (int)MachineWatchInterface.LogType.FinalizeWorkorder;

          using (var reader = cmd.ExecuteReader())
          {
            return LoadLog(reader);
          }
        }
      }
    }

    public List<MachineWatchInterface.LogEntry> GetCompletedPartLogs(DateTime startUTC, DateTime endUTC)
    {
      var searchCompleted = @"
                SELECT Counter FROM stations_mat
                    WHERE MaterialId IN
                        (SELECT stations_mat.MaterialId FROM stations, stations_mat, matdetails
                            WHERE
                                stations.Counter = stations_mat.Counter
                                AND
                                stations.StationLoc = $loadty
                                AND
                                stations.Result = 'UNLOAD'
                                AND
                                stations.Start = 0
                                AND
                                stations.TimeUTC <= $endUTC
                                AND
                                stations.TimeUTC >= $startUTC
                                AND
                                stations_mat.MaterialID = matdetails.MaterialID
                                AND
                                stations_mat.Process = matdetails.NumProcesses
                        )";

      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
              " FROM stations WHERE Counter IN (" + searchCompleted + ") ORDER BY Counter ASC";
          cmd.Parameters.Add("loadty", SqliteType.Integer)
              .Value = (int)MachineWatchInterface.LogType.LoadUnloadCycle;
          cmd.Parameters.Add("endUTC", SqliteType.Integer).Value = endUTC.Ticks;
          cmd.Parameters.Add("startUTC", SqliteType.Integer).Value = startUTC.Ticks;

          using (var reader = cmd.ExecuteReader())
          {
            return LoadLog(reader);
          }
        }
      }
    }

    public DateTime LastPalletCycleTime(string pallet)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT TimeUTC FROM stations where Pallet = $pal AND Result = 'PalletCycle' " +
                               "ORDER BY Counter DESC LIMIT 1";
          cmd.Parameters.Add("pal", SqliteType.Text).Value = pallet;

          var date = cmd.ExecuteScalar();
          if (date == null || date == DBNull.Value)
            return DateTime.MinValue;
          else
            return new DateTime((long)date, DateTimeKind.Utc);
        }
      }
    }

    //Loads the log for the current pallet cycle, which is all events from the last Result = "PalletCycle"
    public List<MachineWatchInterface.LogEntry> CurrentPalletLog(string pallet)
    {
      lock (_cfg)
      {
        using (var trans = _connection.BeginTransaction())
        {
          var ret = CurrentPalletLog(pallet, trans);
          trans.Commit();
          return ret;
        }
      }
    }

    private List<MachineWatchInterface.LogEntry> CurrentPalletLog(string pallet, SqliteTransaction trans)
    {
      string ignoreInvalidCondition =
        "   NOT EXISTS (" +
        "    SELECT 1 FROM program_details d " +
        "      WHERE s.Counter = d.Counter AND d.Key = 'PalletCycleInvalidated'" +
        "   ) AND " +
        "   StationLoc != (" + ((int)MachineWatchInterface.LogType.SwapMaterialOnPallet).ToString() + ")";

      using (var cmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "SELECT MAX(Counter) FROM stations where Pallet = $pal AND Result = 'PalletCycle'";
        cmd.Parameters.Add("pal", SqliteType.Text).Value = pallet;

        var counter = cmd.ExecuteScalar();

        if (counter == DBNull.Value)
        {

          cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
              " FROM stations s " +
              " WHERE Pallet = $pal AND " + ignoreInvalidCondition +
              " ORDER BY Counter ASC";
          using (var reader = cmd.ExecuteReader())
          {
            return LoadLog(reader);
          }

        }
        else
        {

          cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
              " FROM stations s " +
              " WHERE Pallet = $pal AND Counter > $cntr AND " + ignoreInvalidCondition +
              " ORDER BY Counter ASC";
          cmd.Parameters.Add("cntr", SqliteType.Integer).Value = (long)counter;

          using (var reader = cmd.ExecuteReader())
          {
            return LoadLog(reader, trans);
          }
        }
      }

    }

    public IEnumerable<ToolPocketSnapshot> ToolPocketSnapshotForCycle(long counter)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT PocketNumber, Tool, CurrentUse, ToolLife FROM tool_snapshots WHERE Counter = $cntr";
          cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;

          using (var reader = cmd.ExecuteReader())
          {
            var ret = new List<ToolPocketSnapshot>();
            while (reader.Read())
            {
              ret.Add(new ToolPocketSnapshot()
              {
                PocketNumber = reader.GetInt32(0),
                Tool = reader.GetString(1),
                CurrentUse = TimeSpan.FromTicks(reader.GetInt64(2)),
                ToolLife = TimeSpan.FromTicks(reader.GetInt64(3))
              });
            }
            return ret;
          }
        }
      }
    }

    public System.DateTime MaxLogDate()
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT MAX(TimeUTC) FROM stations";

          System.DateTime ret = DateTime.MinValue;

          using (var reader = cmd.ExecuteReader())
          {
            if (reader.Read())
            {
              if (!reader.IsDBNull(0))
              {
                ret = new DateTime(reader.GetInt64(0), DateTimeKind.Utc);
              }
            }
          }

          return ret;
        }
      }
    }

    public string MaxForeignID()
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {

          cmd.CommandText = "SELECT MAX(ForeignID) FROM stations";
          var maxStat = cmd.ExecuteScalar();

          cmd.CommandText = "SELECT MAX(ForeignID) FROM pendingloads";
          var maxLoad = cmd.ExecuteScalar();

          if (maxStat == DBNull.Value && maxLoad == DBNull.Value)
            return "";
          else if (maxStat != DBNull.Value && maxLoad == DBNull.Value)
            return (string)maxStat;
          else if (maxStat == DBNull.Value && maxLoad != DBNull.Value)
            return (string)maxLoad;
          else
          {
            var s = (string)maxStat;
            var l = (string)maxLoad;
            if (s.CompareTo(l) > 0)
              return s;
            else
              return l;
          }
        }
      }
    }

    public string ForeignIDForCounter(long counter)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT ForeignID FROM stations WHERE Counter = $cntr";
          cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;
          var ret = cmd.ExecuteScalar();
          if (ret == DBNull.Value)
            return "";
          else if (ret == null)
            return "";
          else
            return (string)ret;
        }
      }
    }

    public bool CycleExists(DateTime endUTC, string pal, MachineWatchInterface.LogType logTy, string locName, int locNum)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT COUNT(*) FROM stations WHERE " +
              "TimeUTC = $time AND Pallet = $pal AND StationLoc = $loc AND StationNum = $locnum AND StationName = $locname";
          cmd.Parameters.Add("time", SqliteType.Integer).Value = endUTC.Ticks;
          cmd.Parameters.Add("pal", SqliteType.Text).Value = pal;
          cmd.Parameters.Add("loc", SqliteType.Integer).Value = (int)logTy;
          cmd.Parameters.Add("locnum", SqliteType.Integer).Value = locNum;
          cmd.Parameters.Add("locname", SqliteType.Text).Value = locName;

          var ret = cmd.ExecuteScalar();
          if (ret == null || Convert.ToInt32(ret) <= 0)
            return false;
          else
            return true;
        }
      }
    }

    public List<MachineWatchInterface.WorkorderSummary> GetWorkorderSummaries(IEnumerable<string> workorders)
    {
      var countQry = @"
				SELECT matdetails.PartName, COUNT(stations_mat.MaterialID) FROM stations, stations_mat, matdetails
  					WHERE
   						stations.Counter = stations_mat.Counter
   						AND
   						stations.StationLoc = $loadty
                        AND
                        stations.Result = 'UNLOAD'
                        AND
                        stations.Start = 0
   						AND
   						stations_mat.MaterialID = matdetails.MaterialID
   						AND
   						matdetails.Workorder = $workid
                        AND
                        stations_mat.Process == matdetails.NumProcesses
                    GROUP BY
                        matdetails.PartName";

      var serialQry = @"
				SELECT DISTINCT Serial FROM matdetails
				    WHERE
					    matdetails.Workorder = $workid";

      var finalizedQry = @"
                SELECT MAX(TimeUTC) FROM stations
                    WHERE
                        Pallet = ''
                        AND
                        Result = $workid
                        AND
                        StationLoc = $workloc"; //use the (Pallet, Result) index

      var timeQry = @"
                SELECT PartName, StationName, SUM(Elapsed / totcount), SUM(ActiveTime / totcount)
                    FROM
                        (
                            SELECT s.StationName, matdetails.PartName, s.Elapsed, s.ActiveTime,
                                 (SELECT COUNT(*) FROM stations_mat AS m2 WHERE m2.Counter = s.Counter) totcount
                              FROM stations AS s, stations_mat AS m, matdetails
                              WHERE
                                s.Counter = m.Counter
                                AND
                                m.MaterialID = matdetails.MaterialID
                                AND
                                matdetails.Workorder = $workid
                                AND
                                s.Start = 0
                        )
                    GROUP BY PartName, StationName";

      using (var countCmd = _connection.CreateCommand())
      using (var serialCmd = _connection.CreateCommand())
      using (var finalizedCmd = _connection.CreateCommand())
      using (var timeCmd = _connection.CreateCommand())
      {
        countCmd.CommandText = countQry;
        countCmd.Parameters.Add("workid", SqliteType.Text);
        countCmd.Parameters.Add("loadty", SqliteType.Integer)
            .Value = (int)MachineWatchInterface.LogType.LoadUnloadCycle;
        serialCmd.CommandText = serialQry;
        serialCmd.Parameters.Add("workid", SqliteType.Text);
        finalizedCmd.CommandText = finalizedQry;
        finalizedCmd.Parameters.Add("workid", SqliteType.Text);
        finalizedCmd.Parameters.Add("workloc", SqliteType.Integer)
            .Value = (int)MachineWatchInterface.LogType.FinalizeWorkorder;
        timeCmd.CommandText = timeQry;
        timeCmd.Parameters.Add("workid", SqliteType.Text);

        var trans = _connection.BeginTransaction();
        try
        {
          countCmd.Transaction = trans;
          serialCmd.Transaction = trans;
          finalizedCmd.Transaction = trans;
          timeCmd.Transaction = trans;

          var ret = new List<MachineWatchInterface.WorkorderSummary>();
          var partMap = new Dictionary<string, MachineWatchInterface.WorkorderPartSummary>();
          foreach (var w in workorders)
          {
            var summary = new MachineWatchInterface.WorkorderSummary();
            summary.WorkorderId = w;
            partMap.Clear();

            countCmd.Parameters[0].Value = w;
            using (var reader = countCmd.ExecuteReader())
            {
              while (reader.Read())
              {
                var wPart = new MachineWatchInterface.WorkorderPartSummary
                {
                  Part = reader.GetString(0),
                  PartsCompleted = reader.GetInt32(1)
                };
                summary.Parts.Add(wPart);
                partMap.Add(wPart.Part, wPart);
              }
            }

            serialCmd.Parameters[0].Value = w;
            using (var reader = serialCmd.ExecuteReader())
            {
              while (reader.Read())
                summary.Serials.Add(reader.GetString(0));
            }

            finalizedCmd.Parameters[0].Value = w;
            using (var reader = finalizedCmd.ExecuteReader())
            {
              if (reader.Read() && !reader.IsDBNull(0))
              {
                summary.FinalizedTimeUTC =
                  new DateTime(reader.GetInt64(0), DateTimeKind.Utc);
              }
            }

            timeCmd.Parameters[0].Value = w;
            using (var reader = timeCmd.ExecuteReader())
            {
              while (reader.Read())
              {
                var partName = reader.GetString(0);
                var stat = reader.GetString(1);
                //part name should exist because material query should return it
                if (partMap.ContainsKey(partName))
                {
                  var detail = partMap[partName];
                  if (!reader.IsDBNull(2))
                    detail.ElapsedStationTime[stat] = TimeSpan.FromTicks((long)reader.GetDecimal(2));
                  if (!reader.IsDBNull(3))
                    detail.ActiveStationTime[stat] = TimeSpan.FromTicks((long)reader.GetDecimal(3));
                }
              }
            }

            ret.Add(summary);
          }

          trans.Commit();
          return ret;
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public List<string> GetWorkordersForUnique(string jobUnique)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        using (var trans = _connection.BeginTransaction())
        {
          cmd.CommandText = "SELECT DISTINCT Workorder FROM matdetails WHERE UniqueStr = $uniq AND Workorder IS NOT NULL ORDER BY Workorder ASC";
          cmd.Transaction = trans;
          cmd.Parameters.Add("uniq", SqliteType.Text).Value = jobUnique;

          var ret = new List<string>();
          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              ret.Add(reader.GetString(0));
            }
          }

          trans.Commit();
          return ret;
        }
      }
    }

    #endregion

    #region Adding

    public class EventLogMaterial
    {
      public long MaterialID { get; set; }
      public int Process { get; set; }
      public string Face { get; set; }

      public static EventLogMaterial FromLogMat(MachineWatchInterface.LogMaterial m)
      {
        return new EventLogMaterial()
        {
          MaterialID = m.MaterialID,
          Process = m.Process,
          Face = m.Face
        };
      }
    }

    public class ToolPocketSnapshot
    {
      public int PocketNumber { get; set; }
      public string Tool { get; set; }
      public TimeSpan CurrentUse { get; set; }
      public TimeSpan ToolLife { get; set; }

      public static IDictionary<string, MachineWatchInterface.ToolUse> DiffSnapshots(IEnumerable<ToolPocketSnapshot> start, IEnumerable<ToolPocketSnapshot> end)
      {
        if (start == null) start = Enumerable.Empty<ToolPocketSnapshot>();
        if (end == null) end = Enumerable.Empty<ToolPocketSnapshot>();
        var endPockets = new Dictionary<(int, string), ToolPocketSnapshot>();
        foreach (var t in end)
        {
          endPockets[(t.PocketNumber, t.Tool)] = t;
        }

        var tools = new Dictionary<string, MachineWatchInterface.ToolUse>();
        void addUse(string tool, MachineWatchInterface.ToolUse use)
        {
          if (tools.TryGetValue(tool, out var existingUse))
          {
            existingUse.ToolUseDuringCycle += use.ToolUseDuringCycle;
            existingUse.ConfiguredToolLife += use.ConfiguredToolLife;
            existingUse.TotalToolUseAtEndOfCycle += use.TotalToolUseAtEndOfCycle;
            existingUse.ToolChangeOccurred = existingUse.ToolChangeOccurred.GetValueOrDefault() || use.ToolChangeOccurred.GetValueOrDefault();
          }
          else
          {
            tools[tool] = use;
          }
        }

        foreach (var startPocket in start)
        {
          if (endPockets.TryGetValue((startPocket.PocketNumber, startPocket.Tool), out var endPocket))
          {
            endPockets.Remove((startPocket.PocketNumber, startPocket.Tool));

            if (startPocket.CurrentUse < endPocket.CurrentUse)
            {
              // no tool change
              addUse(startPocket.Tool, new MachineWatchInterface.ToolUse()
              {
                ToolUseDuringCycle = endPocket.CurrentUse - startPocket.CurrentUse,
                TotalToolUseAtEndOfCycle = endPocket.CurrentUse,
                ConfiguredToolLife = endPocket.ToolLife,
                ToolChangeOccurred = false
              });
            }
            else if (endPocket.CurrentUse < startPocket.CurrentUse)
            {
              // there was a tool change
              addUse(startPocket.Tool, new MachineWatchInterface.ToolUse()
              {
                ToolUseDuringCycle = TimeSpan.FromTicks(Math.Max(0, startPocket.ToolLife.Ticks - startPocket.CurrentUse.Ticks)) + endPocket.CurrentUse,
                TotalToolUseAtEndOfCycle = endPocket.CurrentUse,
                ConfiguredToolLife = endPocket.ToolLife,
                ToolChangeOccurred = true
              });
            }
            else
            {
              // tool was not used, use same at beginning and end
            }
          }
          else
          {
            // no matching tool at end
            // assume start tool was used until life
            addUse(startPocket.Tool, new MachineWatchInterface.ToolUse()
            {
              ToolUseDuringCycle = TimeSpan.FromTicks(Math.Max(0, startPocket.ToolLife.Ticks - startPocket.CurrentUse.Ticks)),
              TotalToolUseAtEndOfCycle = TimeSpan.Zero,
              ConfiguredToolLife = TimeSpan.Zero,
              ToolChangeOccurred = true
            });
          }
        }

        // now any new tools which appeared
        foreach (var endPocket in endPockets.Values)
        {
          if (endPocket.CurrentUse.Ticks > 0)
          {
            addUse(endPocket.Tool, new MachineWatchInterface.ToolUse()
            {
              ToolUseDuringCycle = endPocket.CurrentUse,
              TotalToolUseAtEndOfCycle = endPocket.CurrentUse,
              ConfiguredToolLife = endPocket.ToolLife,
              ToolChangeOccurred = false
            });
          }
        }

        return tools;
      }
    }

    public class NewEventLogEntry
    {
      public IEnumerable<EventLogMaterial> Material { get; set; }
      public MachineWatchInterface.LogType LogType { get; set; }
      public bool StartOfCycle { get; set; }
      public DateTime EndTimeUTC { get; set; }
      public string LocationName { get; set; }
      public int LocationNum { get; set; }
      public string Pallet { get; set; }
      public string Program { get; set; }
      public string Result { get; set; }
      public bool EndOfRoute { get; set; }
      public TimeSpan ElapsedTime { get; set; } = TimeSpan.FromMinutes(-1); //time from cycle-start to cycle-stop
      public TimeSpan ActiveOperationTime { get; set; } //time that the machining or operation is actually active
      private Dictionary<string, string> _details = new Dictionary<string, string>();
      public IDictionary<string, string> ProgramDetails { get { return _details; } }
      private Dictionary<string, MachineWatchInterface.ToolUse> _tools = new Dictionary<string, MachineWatchInterface.ToolUse>();
      public IDictionary<string, MachineWatchInterface.ToolUse> Tools => _tools;
      public IEnumerable<ToolPocketSnapshot> ToolPockets { get; set; }

      internal MachineWatchInterface.LogEntry ToLogEntry(long newCntr, Func<long, MachineWatchInterface.MaterialDetails> getDetails)
      {
        var e = new MachineWatchInterface.LogEntry(
          cntr: newCntr,
          mat: this.Material.Select(m =>
          {
            var details = getDetails(m.MaterialID);
            return new MachineWatchInterface.LogMaterial(
                matID: m.MaterialID,
                proc: m.Process,
                face: m.Face,
                uniq: details?.JobUnique ?? "",
                part: details?.PartName ?? "",
                numProc: details?.NumProcesses ?? 1,
                serial: details?.Serial ?? "",
                workorder: details?.Workorder ?? ""
            );
          }).ToList(),
          pal: this.Pallet,
          ty: this.LogType,
          locName: this.LocationName,
          locNum: this.LocationNum,
          prog: this.Program,
          start: this.StartOfCycle,
          endTime: this.EndTimeUTC,
          result: this.Result,
          endOfRoute: this.EndOfRoute,
          elapsed: this.ElapsedTime,
          active: this.ActiveOperationTime
        );
        foreach (var d in this.ProgramDetails)
        {
          e.ProgramDetails[d.Key] = d.Value;
        }
        foreach (var t in this.Tools)
        {
          e.Tools[t.Key] = new MachineWatchInterface.ToolUse()
          {
            ToolUseDuringCycle = t.Value.ToolUseDuringCycle,
            TotalToolUseAtEndOfCycle = t.Value.TotalToolUseAtEndOfCycle,
            ConfiguredToolLife = t.Value.ConfiguredToolLife
          };
        }
        return e;
      }

      internal static NewEventLogEntry FromLogEntry(MachineWatchInterface.LogEntry e)
      {
        var ret = new NewEventLogEntry()
        {
          Material = e.Material.Select(EventLogMaterial.FromLogMat),
          Pallet = e.Pallet,
          LogType = e.LogType,
          LocationName = e.LocationName,
          LocationNum = e.LocationNum,
          Program = e.Program,
          StartOfCycle = e.StartOfCycle,
          EndTimeUTC = e.EndTimeUTC,
          Result = e.Result,
          EndOfRoute = e.EndOfRoute,
          ElapsedTime = e.ElapsedTime,
          ActiveOperationTime = e.ActiveOperationTime
        };
        foreach (var d in e.ProgramDetails)
        {
          ret.ProgramDetails[d.Key] = d.Value;
        }
        foreach (var t in e.Tools)
        {
          ret.Tools[t.Key] = new MachineWatchInterface.ToolUse()
          {
            ToolUseDuringCycle = t.Value.ToolUseDuringCycle,
            TotalToolUseAtEndOfCycle = t.Value.TotalToolUseAtEndOfCycle,
            ConfiguredToolLife = t.Value.ConfiguredToolLife
          };
        }
        return ret;
      }
    }

    private MachineWatchInterface.LogEntry AddLogEntry(IDbTransaction trans, NewEventLogEntry log, string foreignID, string origMessage)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "INSERT INTO stations(Pallet, StationLoc, StationName, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, ForeignID,OriginalMessage)" +
            "VALUES ($pal,$loc,$locname,$locnum,$prog,$start,$time,$result,$end,$elapsed,$active,$foreign,$orig)";

        cmd.Parameters.Add("pal", SqliteType.Text).Value = log.Pallet;
        cmd.Parameters.Add("loc", SqliteType.Integer).Value = (int)log.LogType;
        cmd.Parameters.Add("locname", SqliteType.Text).Value = log.LocationName;
        cmd.Parameters.Add("locnum", SqliteType.Integer).Value = log.LocationNum;
        cmd.Parameters.Add("prog", SqliteType.Text).Value = log.Program;
        cmd.Parameters.Add("start", SqliteType.Integer).Value = log.StartOfCycle;
        cmd.Parameters.Add("time", SqliteType.Integer).Value = log.EndTimeUTC.Ticks;
        cmd.Parameters.Add("result", SqliteType.Text).Value = log.Result;
        cmd.Parameters.Add("end", SqliteType.Integer).Value = log.EndOfRoute;
        if (log.ElapsedTime.Ticks >= 0)
          cmd.Parameters.Add("elapsed", SqliteType.Integer).Value = log.ElapsedTime.Ticks;
        else
          cmd.Parameters.Add("elapsed", SqliteType.Integer).Value = DBNull.Value;
        if (log.ActiveOperationTime.Ticks > 0)
          cmd.Parameters.Add("active", SqliteType.Integer).Value = log.ActiveOperationTime.Ticks;
        else
          cmd.Parameters.Add("active", SqliteType.Integer).Value = DBNull.Value;
        if (foreignID == null || foreignID == "")
          cmd.Parameters.Add("foreign", SqliteType.Text).Value = DBNull.Value;
        else
          cmd.Parameters.Add("foreign", SqliteType.Text).Value = foreignID;
        if (origMessage == null || origMessage == "")
          cmd.Parameters.Add("orig", SqliteType.Text).Value = DBNull.Value;
        else
          cmd.Parameters.Add("orig", SqliteType.Text).Value = origMessage;

        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT last_insert_rowid()";
        cmd.Parameters.Clear();
        long ctr = (long)cmd.ExecuteScalar();

        AddMaterial(ctr, log.Material, trans);
        AddProgramDetail(ctr, log.ProgramDetails, trans);
        AddToolUse(ctr, log.Tools, trans);
        AddToolSnapshots(ctr, log.ToolPockets, trans);

        return log.ToLogEntry(ctr, m => this.GetMaterialDetails(m, trans));
      }
    }

    private void AddMaterial(long counter, IEnumerable<EventLogMaterial> mat, IDbTransaction trans)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "INSERT INTO stations_mat(Counter,MaterialID,Process,Face)" +
       "VALUES($cntr,$mat,$proc,$face)";
        cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;
        cmd.Parameters.Add("mat", SqliteType.Integer);
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("face", SqliteType.Text);

        foreach (var m in mat)
        {
          cmd.Parameters[1].Value = m.MaterialID;
          cmd.Parameters[2].Value = m.Process;
          cmd.Parameters[3].Value = m.Face ?? "";
          cmd.ExecuteNonQuery();
        }
      }
    }

    private void AddProgramDetail(long counter, IDictionary<string, string> details, IDbTransaction trans)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "INSERT INTO program_details(Counter,Key,Value) VALUES($cntr,$key,$val)";
        cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;
        cmd.Parameters.Add("key", SqliteType.Text);
        cmd.Parameters.Add("val", SqliteType.Text);

        foreach (var pair in details)
        {
          cmd.Parameters[1].Value = pair.Key;
          cmd.Parameters[2].Value = pair.Value;
          cmd.ExecuteNonQuery();
        }
      }
    }

    private void AddToolUse(long counter, IDictionary<string, MachineWatchInterface.ToolUse> tools, IDbTransaction trans)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "INSERT INTO station_tools(Counter, Tool, UseInCycle, UseAtEndOfCycle, ToolLife, ToolChange) VALUES ($cntr,$tool,$use,$totalUse,$life,$change)";
        cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;
        cmd.Parameters.Add("tool", SqliteType.Text);
        cmd.Parameters.Add("use", SqliteType.Integer);
        cmd.Parameters.Add("totalUse", SqliteType.Integer);
        cmd.Parameters.Add("life", SqliteType.Integer);
        cmd.Parameters.Add("change", SqliteType.Integer);

        foreach (var pair in tools)
        {
          cmd.Parameters[1].Value = pair.Key;
          cmd.Parameters[2].Value = pair.Value.ToolUseDuringCycle.Ticks;
          cmd.Parameters[3].Value = pair.Value.TotalToolUseAtEndOfCycle.Ticks;
          cmd.Parameters[4].Value = pair.Value.ConfiguredToolLife.Ticks;
          cmd.Parameters[5].Value = pair.Value.ToolChangeOccurred.GetValueOrDefault(false);
          cmd.ExecuteNonQuery();
        }
      }
    }

    private void AddToolSnapshots(long counter, IEnumerable<ToolPocketSnapshot> pockets, IDbTransaction trans)
    {
      if (pockets == null || !pockets.Any()) return;

      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "INSERT OR REPLACE INTO tool_snapshots(Counter, PocketNumber, Tool, CurrentUse, ToolLife) VALUES ($cntr,$pocket,$tool,$use,$life)";
        cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;
        cmd.Parameters.Add("pocket", SqliteType.Integer);
        cmd.Parameters.Add("tool", SqliteType.Text);
        cmd.Parameters.Add("use", SqliteType.Integer);
        cmd.Parameters.Add("life", SqliteType.Integer);

        foreach (var pocket in pockets)
        {
          cmd.Parameters[1].Value = pocket.PocketNumber;
          cmd.Parameters[2].Value = pocket.Tool;
          cmd.Parameters[3].Value = pocket.CurrentUse.Ticks;
          cmd.Parameters[4].Value = pocket.ToolLife.Ticks;
          cmd.ExecuteNonQuery();
        }
      }
    }

    private MachineWatchInterface.LogEntry AddEntryInTransaction(Func<IDbTransaction, MachineWatchInterface.LogEntry> f, string foreignId = "")
    {
      MachineWatchInterface.LogEntry log;
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          log = f(trans);
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
      _cfg.OnNewLogEntry(log, foreignId, this);
      return log;
    }

    private IEnumerable<MachineWatchInterface.LogEntry> AddEntryInTransaction(Func<IDbTransaction, IReadOnlyList<MachineWatchInterface.LogEntry>> f, string foreignId = "")
    {
      IEnumerable<MachineWatchInterface.LogEntry> logs;
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          logs = f(trans).ToList();
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
      foreach (var l in logs) _cfg.OnNewLogEntry(l, foreignId, this);
      return logs;
    }

    internal MachineWatchInterface.LogEntry AddLogEntryFromUnitTest(MachineWatchInterface.LogEntry log, string foreignId = null, string origMessage = null)
    {
      return AddEntryInTransaction(trans =>
          AddLogEntry(trans, NewEventLogEntry.FromLogEntry(log), foreignId, origMessage)
      );
    }


    public MachineWatchInterface.LogEntry RecordLoadStart(
        IEnumerable<EventLogMaterial> mats,
        string pallet,
        int lulNum,
        DateTime timeUTC,
        string foreignId = null,
        string originalMessage = null
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = mats,
        Pallet = pallet,
        LogType = MachineWatchInterface.LogType.LoadUnloadCycle,
        LocationName = "L/U",
        LocationNum = lulNum,
        Program = "LOAD",
        StartOfCycle = true,
        EndTimeUTC = timeUTC,
        Result = "LOAD",
        EndOfRoute = false
      };
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, foreignId, originalMessage));
    }

    public IEnumerable<MachineWatchInterface.LogEntry> RecordLoadEnd(
        IEnumerable<EventLogMaterial> mats,
        string pallet,
        int lulNum,
        DateTime timeUTC,
        TimeSpan elapsed,
        TimeSpan active,
        string foreignId = null,
        string originalMessage = null
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = mats,
        Pallet = pallet,
        LogType = MachineWatchInterface.LogType.LoadUnloadCycle,
        LocationName = "L/U",
        LocationNum = lulNum,
        Program = "LOAD",
        StartOfCycle = false,
        EndTimeUTC = timeUTC,
        Result = "LOAD",
        ElapsedTime = elapsed,
        ActiveOperationTime = active,
        EndOfRoute = false
      };
      return AddEntryInTransaction(trans =>
      {
        var logs = new List<MachineWatchInterface.LogEntry>();
        foreach (var mat in mats)
        {
          var prevProcMat = new EventLogMaterial() { MaterialID = mat.MaterialID, Process = mat.Process - 1, Face = "" };
          logs.AddRange(RemoveFromAllQueues(trans, prevProcMat, operatorName: null, timeUTC: timeUTC));
        }
        logs.Add(AddLogEntry(trans, log, foreignId, originalMessage));
        return logs;
      });
    }

    public MachineWatchInterface.LogEntry RecordUnloadStart(
        IEnumerable<EventLogMaterial> mats,
        string pallet,
        int lulNum,
        DateTime timeUTC,
        string foreignId = null,
        string originalMessage = null
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = mats,
        Pallet = pallet,
        LogType = MachineWatchInterface.LogType.LoadUnloadCycle,
        LocationName = "L/U",
        LocationNum = lulNum,
        Program = "UNLOAD",
        StartOfCycle = true,
        EndTimeUTC = timeUTC,
        Result = "UNLOAD",
        EndOfRoute = false
      };
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, foreignId, originalMessage));
    }

    public IEnumerable<MachineWatchInterface.LogEntry> RecordUnloadEnd(
        IEnumerable<EventLogMaterial> mats,
        string pallet,
        int lulNum,
        DateTime timeUTC,
        TimeSpan elapsed,
        TimeSpan active,
        Dictionary<long, string> unloadIntoQueues = null, // key is material id, value is queue name
        string foreignId = null,
        string originalMessage = null
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = mats,
        Pallet = pallet,
        LogType = MachineWatchInterface.LogType.LoadUnloadCycle,
        LocationName = "L/U",
        LocationNum = lulNum,
        Program = "UNLOAD",
        StartOfCycle = false,
        EndTimeUTC = timeUTC,
        ElapsedTime = elapsed,
        ActiveOperationTime = active,
        Result = "UNLOAD",
        EndOfRoute = true
      };
      return AddEntryInTransaction(trans =>
      {
        var msgs = new List<MachineWatchInterface.LogEntry>();
        if (unloadIntoQueues != null)
        {
          foreach (var mat in mats)
          {
            if (unloadIntoQueues.ContainsKey(mat.MaterialID))
            {
              msgs.AddRange(AddToQueue(trans, mat, unloadIntoQueues[mat.MaterialID], -1, operatorName: null, timeUTC: timeUTC, reason: "Unloaded"));
            }
          }
        }
        msgs.Add(AddLogEntry(trans, log, foreignId, originalMessage));
        return msgs;
      });
    }

    public MachineWatchInterface.LogEntry RecordManualWorkAtLULStart(
        IEnumerable<EventLogMaterial> mats,
        string pallet,
        int lulNum,
        DateTime timeUTC,
        string operationName,
        string foreignId = null,
        string originalMessage = null
    )
    {
      if (operationName == "LOAD" || operationName == "UNLOAD")
      {
        throw new ArgumentException("ManualWorkAtLUL operation cannot be LOAD or UNLOAD", "operationName");
      }
      var log = new NewEventLogEntry()
      {
        Material = mats,
        Pallet = pallet,
        LogType = MachineWatchInterface.LogType.LoadUnloadCycle,
        LocationName = "L/U",
        LocationNum = lulNum,
        Program = operationName,
        StartOfCycle = true,
        EndTimeUTC = timeUTC,
        Result = operationName,
        EndOfRoute = false
      };
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, foreignId, originalMessage));
    }

    public MachineWatchInterface.LogEntry RecordManualWorkAtLULEnd(
        IEnumerable<EventLogMaterial> mats,
        string pallet,
        int lulNum,
        DateTime timeUTC,
        TimeSpan elapsed,
        TimeSpan active,
        string operationName,
        string foreignId = null,
        string originalMessage = null
    )
    {
      if (operationName == "LOAD" || operationName == "UNLOAD")
      {
        throw new ArgumentException("ManualWorkAtLUL operation cannot be LOAD or UNLOAD", "operationName");
      }
      var log = new NewEventLogEntry()
      {
        Material = mats,
        Pallet = pallet,
        LogType = MachineWatchInterface.LogType.LoadUnloadCycle,
        LocationName = "L/U",
        LocationNum = lulNum,
        Program = operationName,
        StartOfCycle = false,
        EndTimeUTC = timeUTC,
        ElapsedTime = elapsed,
        ActiveOperationTime = active,
        Result = operationName,
        EndOfRoute = false
      };
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, foreignId, originalMessage));
    }

    public MachineWatchInterface.LogEntry RecordMachineStart(
        IEnumerable<EventLogMaterial> mats,
        string pallet,
        string statName,
        int statNum,
        string program,
        DateTime timeUTC,
        IDictionary<string, string> extraData = null,
        IEnumerable<ToolPocketSnapshot> pockets = null,
        string foreignId = null,
        string originalMessage = null
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = mats,
        Pallet = pallet,
        LogType = MachineWatchInterface.LogType.MachineCycle,
        LocationName = statName,
        LocationNum = statNum,
        Program = program,
        StartOfCycle = true,
        EndTimeUTC = timeUTC,
        Result = "",
        EndOfRoute = false,
        ToolPockets = pockets
      };
      if (extraData != null)
      {
        foreach (var k in extraData)
          log.ProgramDetails[k.Key] = k.Value;
      }
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, foreignId, originalMessage));
    }

    public MachineWatchInterface.LogEntry RecordMachineEnd(
        IEnumerable<EventLogMaterial> mats,
        string pallet,
        string statName,
        int statNum,
        string program,
        string result,
        DateTime timeUTC,
        TimeSpan elapsed,
        TimeSpan active,
        IDictionary<string, string> extraData = null,
        IDictionary<string, MachineWatchInterface.ToolUse> tools = null,
        IEnumerable<ToolPocketSnapshot> pockets = null,
        string foreignId = null,
        string originalMessage = null
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = mats,
        Pallet = pallet,
        LogType = MachineWatchInterface.LogType.MachineCycle,
        LocationName = statName,
        LocationNum = statNum,
        Program = program,
        StartOfCycle = false,
        EndTimeUTC = timeUTC,
        Result = result,
        ElapsedTime = elapsed,
        ActiveOperationTime = active,
        EndOfRoute = false,
        ToolPockets = pockets
      };
      if (extraData != null)
      {
        foreach (var k in extraData)
          log.ProgramDetails[k.Key] = k.Value;
      }
      if (tools != null)
      {
        foreach (var t in tools)
          log.Tools[t.Key] = t.Value;
      }
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, foreignId, originalMessage));
    }

    public MachineWatchInterface.LogEntry RecordPalletArriveRotaryInbound(
      IEnumerable<EventLogMaterial> mats,
      string pallet,
      string statName,
      int statNum,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return AddEntryInTransaction(trans =>
        AddLogEntry(trans,
        new NewEventLogEntry()
        {
          Material = mats,
          Pallet = pallet,
          LogType = MachineWatchInterface.LogType.PalletOnRotaryInbound,
          LocationName = statName,
          LocationNum = statNum,
          Program = "Arrive",
          StartOfCycle = true,
          EndTimeUTC = timeUTC,
          Result = "Arrive",
          EndOfRoute = false,
        },
        foreignId, originalMessage
        )
      );
    }

    public MachineWatchInterface.LogEntry RecordPalletDepartRotaryInbound(
      IEnumerable<EventLogMaterial> mats,
      string pallet,
      string statName,
      int statNum,
      DateTime timeUTC,
      TimeSpan elapsed,
      bool rotateIntoWorktable,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return AddEntryInTransaction(trans =>
        AddLogEntry(trans,
        new NewEventLogEntry()
        {
          Material = mats,
          Pallet = pallet,
          LogType = MachineWatchInterface.LogType.PalletOnRotaryInbound,
          LocationName = statName,
          LocationNum = statNum,
          Program = "Depart",
          StartOfCycle = false,
          EndTimeUTC = timeUTC,
          Result = rotateIntoWorktable ? "RotateIntoWorktable" : "LeaveMachine",
          ElapsedTime = elapsed,
          EndOfRoute = false,
        },
        foreignId, originalMessage
        )
      );
    }

    public MachineWatchInterface.LogEntry RecordPalletArriveStocker(
      IEnumerable<EventLogMaterial> mats,
      string pallet,
      int stockerNum,
      DateTime timeUTC,
      bool waitForMachine,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return AddEntryInTransaction(trans =>
        AddLogEntry(trans,
        new NewEventLogEntry()
        {
          Material = mats,
          Pallet = pallet,
          LogType = MachineWatchInterface.LogType.PalletInStocker,
          LocationName = "Stocker",
          LocationNum = stockerNum,
          Program = "Arrive",
          StartOfCycle = true,
          EndTimeUTC = timeUTC,
          Result = waitForMachine ? "WaitForMachine" : "WaitForUnload",
          EndOfRoute = false,
        },
        foreignId, originalMessage
        )
      );
    }

    public MachineWatchInterface.LogEntry RecordPalletDepartStocker(
      IEnumerable<EventLogMaterial> mats,
      string pallet,
      int stockerNum,
      DateTime timeUTC,
      bool waitForMachine,
      TimeSpan elapsed,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return AddEntryInTransaction(trans =>
        AddLogEntry(trans,
        new NewEventLogEntry()
        {
          Material = mats,
          Pallet = pallet,
          LogType = MachineWatchInterface.LogType.PalletInStocker,
          LocationName = "Stocker",
          LocationNum = stockerNum,
          Program = "Depart",
          StartOfCycle = false,
          EndTimeUTC = timeUTC,
          Result = waitForMachine ? "WaitForMachine" : "WaitForUnload",
          ElapsedTime = elapsed,
          EndOfRoute = false,
        },
        foreignId, originalMessage
        )
      );
    }

    public MachineWatchInterface.LogEntry RecordSerialForMaterialID(long materialID, int proc, string serial)
    {
      var mat = new EventLogMaterial() { MaterialID = materialID, Process = proc, Face = "" };
      return RecordSerialForMaterialID(mat, serial, DateTime.UtcNow);
    }

    public MachineWatchInterface.LogEntry RecordSerialForMaterialID(EventLogMaterial mat, string serial)
    {
      return RecordSerialForMaterialID(mat, serial, DateTime.UtcNow);
    }

    public MachineWatchInterface.LogEntry RecordSerialForMaterialID(EventLogMaterial mat, string serial, DateTime endTimeUTC)
    {
      var log = new NewEventLogEntry()
      {
        Material = new[] { mat },
        Pallet = "",
        LogType = MachineWatchInterface.LogType.PartMark,
        LocationName = "Mark",
        LocationNum = 1,
        Program = "MARK",
        StartOfCycle = false,
        EndTimeUTC = endTimeUTC,
        Result = serial,
        EndOfRoute = false
      };
      return AddEntryInTransaction(trans =>
      {
        RecordSerialForMaterialID(trans, mat.MaterialID, serial);
        return AddLogEntry(trans, log, null, null);
      });
    }

    // For backwards compatibility
    public MachineWatchInterface.LogEntry RecordWorkorderForMaterialID(long materialID, int proc, string workorder)
    {
      var mat = new EventLogMaterial() { MaterialID = materialID, Process = proc, Face = "" };
      return RecordWorkorderForMaterialID(mat, workorder, DateTime.UtcNow);
    }

    public MachineWatchInterface.LogEntry RecordWorkorderForMaterialID(EventLogMaterial mat, string workorder)
    {
      return RecordWorkorderForMaterialID(mat, workorder, DateTime.UtcNow);
    }

    public MachineWatchInterface.LogEntry RecordWorkorderForMaterialID(EventLogMaterial mat, string workorder, DateTime recordUtc)
    {
      var log = new NewEventLogEntry()
      {
        Material = new[] { mat },
        Pallet = "",
        LogType = MachineWatchInterface.LogType.OrderAssignment,
        LocationName = "Order",
        LocationNum = 1,
        Program = "",
        StartOfCycle = false,
        EndTimeUTC = recordUtc,
        Result = workorder,
        EndOfRoute = false
      };
      return AddEntryInTransaction(trans =>
      {
        RecordWorkorderForMaterialID(trans, mat.MaterialID, workorder);
        return AddLogEntry(trans, log, null, null);
      });
    }

    public MachineWatchInterface.LogEntry RecordInspectionCompleted(
        long materialID,
        int process,
        int inspectionLocNum,
        string inspectionType,
        bool success,
        IDictionary<string, string> extraData,
        TimeSpan elapsed,
        TimeSpan active)
    {
      var mat = new EventLogMaterial() { MaterialID = materialID, Process = process, Face = "" };
      return RecordInspectionCompleted(mat, inspectionLocNum, inspectionType, success, extraData, elapsed, active, DateTime.UtcNow);
    }

    public MachineWatchInterface.LogEntry RecordInspectionCompleted(
        EventLogMaterial mat,
        int inspectionLocNum,
        string inspectionType,
        bool success,
        IDictionary<string, string> extraData,
        TimeSpan elapsed,
        TimeSpan active)
    {
      return RecordInspectionCompleted(mat, inspectionLocNum, inspectionType, success, extraData, elapsed, active, DateTime.UtcNow);
    }

    public MachineWatchInterface.LogEntry RecordInspectionCompleted(
        EventLogMaterial mat,
        int inspectionLocNum,
        string inspectionType,
        bool success,
        IDictionary<string, string> extraData,
        TimeSpan elapsed,
        TimeSpan active,
        DateTime inspectTimeUTC)
    {
      var log = new NewEventLogEntry()
      {
        Material = new[] { mat },
        Pallet = "",
        LogType = MachineWatchInterface.LogType.InspectionResult,
        LocationName = "Inspection",
        LocationNum = inspectionLocNum,
        Program = inspectionType,
        StartOfCycle = false,
        EndTimeUTC = inspectTimeUTC,
        Result = success.ToString(),
        EndOfRoute = false,
        ElapsedTime = elapsed,
        ActiveOperationTime = active
      };
      foreach (var x in extraData) log.ProgramDetails.Add(x.Key, x.Value);

      return AddEntryInTransaction(trans => AddLogEntry(trans, log, null, null));
    }

    public MachineWatchInterface.LogEntry RecordWashCompleted(
        long materialID,
        int process,
        int washLocNum,
        IDictionary<string, string> extraData,
        TimeSpan elapsed,
        TimeSpan active
    )
    {
      var mat = new EventLogMaterial() { MaterialID = materialID, Process = process, Face = "" };
      return RecordWashCompleted(mat, washLocNum, extraData, elapsed, active, DateTime.UtcNow);
    }

    public MachineWatchInterface.LogEntry RecordWashCompleted(
        EventLogMaterial mat,
        int washLocNum,
        IDictionary<string, string> extraData,
        TimeSpan elapsed,
        TimeSpan active
    )
    {
      return RecordWashCompleted(mat, washLocNum, extraData, elapsed, active, DateTime.UtcNow);
    }

    public MachineWatchInterface.LogEntry RecordWashCompleted(
        EventLogMaterial mat,
        int washLocNum,
        IDictionary<string, string> extraData,
        TimeSpan elapsed,
        TimeSpan active,
        DateTime completeTimeUTC)
    {
      var log = new NewEventLogEntry()
      {
        Material = new[] { mat },
        Pallet = "",
        LogType = MachineWatchInterface.LogType.Wash,
        LocationName = "Wash",
        LocationNum = washLocNum,
        Program = "",
        StartOfCycle = false,
        EndTimeUTC = completeTimeUTC,
        Result = "",
        EndOfRoute = false,
        ElapsedTime = elapsed,
        ActiveOperationTime = active
      };
      foreach (var x in extraData) log.ProgramDetails.Add(x.Key, x.Value);
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, null, null));
    }

    public MachineWatchInterface.LogEntry RecordFinalizedWorkorder(string workorder)
    {
      return RecordFinalizedWorkorder(workorder, DateTime.UtcNow);

    }
    public MachineWatchInterface.LogEntry RecordFinalizedWorkorder(string workorder, DateTime finalizedUTC)
    {
      var log = new NewEventLogEntry()
      {
        Material = new EventLogMaterial[] { },
        Pallet = "",
        LogType = MachineWatchInterface.LogType.FinalizeWorkorder,
        LocationName = "FinalizeWorkorder",
        LocationNum = 1,
        Program = "",
        StartOfCycle = false,
        EndTimeUTC = finalizedUTC,
        Result = workorder,
        EndOfRoute = false
      };
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, null, null));
    }

    public IEnumerable<MachineWatchInterface.LogEntry> RecordAddMaterialToQueue(
        EventLogMaterial mat, string queue, int position, string operatorName, string reason, DateTime? timeUTC = null)
    {
      return AddEntryInTransaction(trans =>
          AddToQueue(trans, mat, queue, position, operatorName: operatorName, timeUTC: timeUTC ?? DateTime.UtcNow, reason: reason)
      );
    }

    public IEnumerable<MachineWatchInterface.LogEntry> RecordAddMaterialToQueue(
        long matID, int process, string queue, int position, string operatorName, string reason, DateTime? timeUTC = null)
    {
      return AddEntryInTransaction(trans =>
          AddToQueue(trans, matID, process, queue, position, operatorName: operatorName, timeUTC: timeUTC ?? DateTime.UtcNow, reason: reason)
      );
    }


    public IEnumerable<MachineWatchInterface.LogEntry> RecordRemoveMaterialFromAllQueues(
        EventLogMaterial mat, string operatorName = null, DateTime? timeUTC = null)
    {
      return AddEntryInTransaction(trans =>
          RemoveFromAllQueues(trans, mat, operatorName, timeUTC ?? DateTime.UtcNow)
      );
    }

    public IEnumerable<MachineWatchInterface.LogEntry> RecordRemoveMaterialFromAllQueues(
        long matID, int process, string operatorName = null, DateTime? timeUTC = null)
    {
      return AddEntryInTransaction(trans =>
          RemoveFromAllQueues(trans, matID, process, operatorName, timeUTC ?? DateTime.UtcNow)
      );
    }

    public MachineWatchInterface.LogEntry SignalMaterialForQuarantine(
      EventLogMaterial mat,
      string pallet,
      string queue,
      DateTime? timeUTC = null,
      string operatorName = null,
      string foreignId = null,
      string originalMessage = null
    )
    {
      var log =
        new NewEventLogEntry()
        {
          Material = new[] { mat },
          Pallet = pallet,
          LogType = MachineWatchInterface.LogType.SignalQuarantine,
          LocationName = queue,
          LocationNum = -1,
          Program = "QuarantineAfterUnload",
          StartOfCycle = false,
          EndTimeUTC = timeUTC ?? DateTime.UtcNow,
          Result = "QuarantineAfterUnload",
          EndOfRoute = false,
        };

      if (!string.IsNullOrEmpty(operatorName))
      {
        log.ProgramDetails["operator"] = operatorName;
      }
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, foreignId, originalMessage));
    }

    public MachineWatchInterface.LogEntry RecordGeneralMessage(
        EventLogMaterial mat, string program, string result, string pallet = "", DateTime? timeUTC = null, string foreignId = null,
        string originalMessage = null,
        IDictionary<string, string> extraData = null
        )
    {
      var log = new NewEventLogEntry()
      {
        Material = mat != null ? new[] { mat } : new EventLogMaterial[] { },
        Pallet = pallet,
        LogType = MachineWatchInterface.LogType.GeneralMessage,
        LocationName = "Message",
        LocationNum = 1,
        Program = program,
        StartOfCycle = false,
        EndTimeUTC = timeUTC ?? DateTime.UtcNow,
        Result = result,
        EndOfRoute = false
      };
      if (extraData != null)
      {
        foreach (var x in extraData) log.ProgramDetails.Add(x.Key, x.Value);
      }
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, foreignId, originalMessage));
    }

    public MachineWatchInterface.LogEntry RecordOperatorNotes(long materialId, int process, string notes, string operatorName)
    {
      return RecordOperatorNotes(materialId, process, notes, operatorName, null);
    }
    public MachineWatchInterface.LogEntry RecordOperatorNotes(long materialId, int process, string notes, string operatorName, DateTime? timeUtc)
    {
      var extra = new Dictionary<string, string>();
      extra["note"] = notes;
      if (!string.IsNullOrEmpty(operatorName))
      {
        extra["operator"] = operatorName;
      }
      return RecordGeneralMessage(
        mat: new EventLogMaterial() { MaterialID = materialId, Process = process, Face = "" },
        program: "OperatorNotes",
        result: "Operator Notes",
        timeUTC: timeUtc,
        extraData: extra
      );
    }

    public IEnumerable<MachineWatchInterface.LogEntry> RawAddLogEntries(IEnumerable<NewEventLogEntry> logs, string foreignId = null, string origMessage = null)
    {
      return AddEntryInTransaction(trans => logs.Select(e => AddLogEntry(trans, e, foreignId, origMessage)).ToList(), foreignId);
    }

    public class SwapMaterialResult
    {
      public IEnumerable<MachineWatchInterface.LogEntry> ChangedLogEntries { get; set; }
      public IEnumerable<MachineWatchInterface.LogEntry> NewLogEntries { get; set; }
    }

    public SwapMaterialResult SwapMaterialInCurrentPalletCycle(
      string pallet,
      long oldMatId,
      long newMatId,
      string operatorName,
      DateTime? timeUTC = null
    )
    {
      var newLogEntries = new List<MachineWatchInterface.LogEntry>();
      var changedLogEntries = new List<MachineWatchInterface.LogEntry>();

      lock (_cfg)
      {
        using (var updateMatsCmd = _connection.CreateCommand())
        using (var trans = _connection.BeginTransaction())
        {
          updateMatsCmd.Transaction = trans;

          // get old material details
          var oldMatDetails = GetMaterialDetails(oldMatId, trans);
          if (oldMatDetails == null || oldMatDetails.Paths.Count == 0)
          {
            throw new ConflictRequestException("Unable to find material");
          }

          // load old events
          var oldEvents = CurrentPalletLog(pallet, trans);
          var oldMatProcM = oldEvents.SelectMany(e => e.Material).Where(m => m.MaterialID == oldMatId).Max(m => (int?)m.Process);
          if (!oldMatProcM.HasValue)
          {
            throw new ConflictRequestException("Unable to find material, or material not currently on a pallet");
          }
          var oldMatProc = oldMatProcM.Value;

          // check new material path matches
          var newMatDetails = GetMaterialDetails(newMatId, trans);
          if (newMatDetails == null)
          {
            throw new ConflictRequestException("Unable to find new material");
          }
          var newMatIsUnassigned = string.IsNullOrEmpty(newMatDetails.JobUnique);
          if (newMatIsUnassigned == false && oldMatDetails.JobUnique != newMatDetails.JobUnique)
          {
            throw new ConflictRequestException("Overriding material on pallet must use material from the same job");
          }
          // TODO: Check if raw material matches

          // perform the swap
          updateMatsCmd.CommandText = "UPDATE stations_mat SET MaterialID = $newmat WHERE Counter = $cntr AND MaterialID = $oldmat";
          updateMatsCmd.Parameters.Add("newmat", SqliteType.Integer).Value = newMatId;
          updateMatsCmd.Parameters.Add("cntr", SqliteType.Integer);
          updateMatsCmd.Parameters.Add("oldmat", SqliteType.Integer).Value = oldMatId;

          foreach (var evt in oldEvents)
          {
            if (evt.Material.Any(m => m.MaterialID == oldMatId))
            {
              updateMatsCmd.Parameters[1].Value = evt.Counter;
              updateMatsCmd.ExecuteNonQuery();

              changedLogEntries.Add(new MachineWatchInterface.LogEntry(evt,
                newMats: evt.Material.Select(m =>
                  m.MaterialID == oldMatId
                    ? new MachineWatchInterface.LogMaterial(
                        matID: newMatId,
                        uniq: oldMatDetails.JobUnique,
                        proc: oldMatProc,
                        part: newMatDetails.PartName,
                        numProc: oldMatDetails.NumProcesses,
                        serial: newMatDetails.Serial ?? "",
                        workorder: newMatDetails.Workorder ?? "",
                        face: m.Face)
                    : m
                )
              ));
            }
          }

          // update job assignment
          if (newMatIsUnassigned)
          {
            using (var setJobCmd = _connection.CreateCommand())
            {
              setJobCmd.Transaction = trans;
              setJobCmd.CommandText = "UPDATE matdetails SET UniqueStr = $uniq, PartName = $part, NumProcesses = $numproc WHERE MaterialID = $mat";
              var uParam = setJobCmd.Parameters.Add("uniq", SqliteType.Text);
              setJobCmd.Parameters.Add("part", SqliteType.Text).Value = oldMatDetails.PartName;
              setJobCmd.Parameters.Add("numproc", SqliteType.Integer).Value = oldMatDetails.NumProcesses;
              var matParam = setJobCmd.Parameters.Add("mat", SqliteType.Integer);

              uParam.Value = oldMatDetails.JobUnique;
              matParam.Value = newMatId;
              setJobCmd.ExecuteNonQuery();

              uParam.Value = DBNull.Value;
              matParam.Value = oldMatId;
              setJobCmd.ExecuteNonQuery();
            }
          }

          // Record a message for the override
          var time = timeUTC ?? DateTime.UtcNow;

          var newMsg = new NewEventLogEntry()
          {
            Material = new EventLogMaterial[] {
              new EventLogMaterial() { MaterialID = oldMatId, Process = oldMatProc, Face = "" },
              new EventLogMaterial() { MaterialID = newMatId, Process = oldMatProc, Face = "" },
             },
            Pallet = pallet,
            LogType = MachineWatchInterface.LogType.SwapMaterialOnPallet,
            LocationName = "SwapMatOnPallet",
            LocationNum = 1,
            Program = "SwapMatOnPallet",
            StartOfCycle = false,
            EndTimeUTC = time,
            Result = "Replace " + (oldMatDetails?.Serial ?? "material") + " with " + (newMatDetails?.Serial ?? "material") + " on pallet " + pallet,
            EndOfRoute = false
          };
          newLogEntries.Add(AddLogEntry(trans, newMsg, null, null));

          // update queues
          var removeQueueEvts = RemoveFromAllQueues(trans, matID: newMatId, process: oldMatProc - 1, operatorName: operatorName, time);
          newLogEntries.AddRange(removeQueueEvts);

          var oldMatPutInQueue =
            removeQueueEvts
            .Where(e => e.LogType == MachineWatchInterface.LogType.RemoveFromQueue && !string.IsNullOrEmpty(e.LocationName))
            .Select(e => e.LocationName)
            .FirstOrDefault()
            ?? _cfg.Settings.QuarantineQueue;

          if (!string.IsNullOrEmpty(oldMatPutInQueue))
          {
            newLogEntries.AddRange(AddToQueue(trans,
              matId: oldMatId,
              process: oldMatProc - 1,
              queue: oldMatPutInQueue,
              position: -1,
              operatorName: operatorName,
              timeUTC: time,
              reason: "SwapMaterial"
            ));
          }

          //update paths
          if (oldMatDetails.Paths.TryGetValue(oldMatProc, out var oldPath))
          {
            using (var newPathCmd = _connection.CreateCommand())
            {
              newPathCmd.Transaction = trans;
              newPathCmd.CommandText = "INSERT OR REPLACE INTO mat_path_details(MaterialID, Process, Path) VALUES ($mid, $proc, $path)";
              newPathCmd.Parameters.Add("mid", SqliteType.Integer).Value = newMatId;
              newPathCmd.Parameters.Add("proc", SqliteType.Integer).Value = oldMatProc;
              newPathCmd.Parameters.Add("path", SqliteType.Integer).Value = oldPath;
              newPathCmd.ExecuteNonQuery();
            }

            if (newMatIsUnassigned || oldMatProc >= 2)
            {
              using (var delPathCmd = _connection.CreateCommand())
              {
                delPathCmd.Transaction = trans;
                delPathCmd.CommandText = "DELETE FROM mat_path_details WHERE MaterialID = $mid AND Process = $proc";
                delPathCmd.Parameters.Add("mid", SqliteType.Integer).Value = oldMatId;
                delPathCmd.Parameters.Add("proc", SqliteType.Integer).Value = oldMatProc;
                delPathCmd.ExecuteNonQuery();
              }
            }
          }

          trans.Commit();
        }
      }

      foreach (var l in newLogEntries) _cfg.OnNewLogEntry(l, null, this);

      return new SwapMaterialResult()
      {
        ChangedLogEntries = changedLogEntries,
        NewLogEntries = newLogEntries
      };
    }

    private static readonly string LogTypesToCheckForNextProcess = string.Join(",", new int[] {
        (int)MachineWatchInterface.LogType.AddToQueue,
        (int)MachineWatchInterface.LogType.RemoveFromQueue,
        (int)MachineWatchInterface.LogType.LoadUnloadCycle,
        (int)MachineWatchInterface.LogType.MachineCycle
      });

    public IEnumerable<MachineWatchInterface.LogEntry> InvalidatePalletCycle(
      long matId,
      int process,
      string oldMatPutInQueue,
      string operatorName,
      DateTime? timeUTC = null
    )
    {
      var newLogEntries = new List<MachineWatchInterface.LogEntry>();

      lock (_cfg)
      {
        using (var getCycles = _connection.CreateCommand())
        using (var getMatsCmd = _connection.CreateCommand())
        using (var updateEvtCmd = _connection.CreateCommand())
        using (var addMessageCmd = _connection.CreateCommand())
        using (var trans = _connection.BeginTransaction())
        {
          getCycles.CommandText = "SELECT s.Counter, s.Pallet FROM stations s WHERE " +
            " EXISTS (" +
            "   SELECT 1 FROM stations_mat m WHERE s.Counter = m.Counter AND m.MaterialID = $matid AND m.Process = $proc" +
            " ) AND " +
            " s.StationLoc IN (" + LogTypesToCheckForNextProcess + ") AND " +
            " NOT EXISTS(" +
            "   SELECT 1 FROM program_details d WHERE s.Counter = d.Counter AND d.Key = 'PalletCycleInvalidated'" +
            " )"
          ;
          getCycles.Parameters.Add("matid", SqliteType.Integer).Value = matId;
          getCycles.Parameters.Add("proc", SqliteType.Integer).Value = process;
          getCycles.Transaction = trans;

          getMatsCmd.CommandText = "SELECT MaterialID FROM stations_mat WHERE Counter = $cntr";
          getMatsCmd.Parameters.Add("cntr", SqliteType.Integer);
          getMatsCmd.Transaction = trans;

          updateEvtCmd.CommandText = "UPDATE stations SET ActiveTime = 0 WHERE Counter = $cntr";
          updateEvtCmd.Parameters.Add("cntr", SqliteType.Integer);
          updateEvtCmd.Transaction = trans;

          addMessageCmd.CommandText = "INSERT OR REPLACE INTO program_details(Counter, Key, Value) VALUES ($cntr,'PalletCycleInvalidated','1')";
          addMessageCmd.Parameters.Add("cntr", SqliteType.Integer);
          addMessageCmd.Transaction = trans;

          // load old events
          string pallet = "";
          var invalidatedCntrs = new List<long>();
          var allMatIds = new HashSet<long>();
          using (var reader = getCycles.ExecuteReader())
          {
            while (reader.Read())
            {
              if (!reader.IsDBNull(1) && !string.IsNullOrEmpty(reader.GetString(1)))
              {
                pallet = reader.GetString(1);
              }
              var cntr = reader.GetInt64(0);
              invalidatedCntrs.Add(cntr);

              getMatsCmd.Parameters[0].Value = cntr;
              using (var matIdReader = getMatsCmd.ExecuteReader())
              {
                while (matIdReader.Read()) allMatIds.Add(matIdReader.GetInt64(0));
              }

              updateEvtCmd.Parameters[0].Value = cntr;
              updateEvtCmd.ExecuteNonQuery();

              addMessageCmd.Parameters[0].Value = cntr;
              addMessageCmd.ExecuteNonQuery();
            }
          }

          // record events
          var time = timeUTC ?? DateTime.UtcNow;

          var newMsg = new NewEventLogEntry()
          {
            Material = allMatIds.Select(m => new EventLogMaterial() { MaterialID = m, Process = process, Face = "" }),
            Pallet = "",
            LogType = MachineWatchInterface.LogType.InvalidateCycle,
            LocationName = "InvalidateCycle",
            LocationNum = 1,
            Program = "InvalidateCycle",
            StartOfCycle = false,
            EndTimeUTC = time,
            Result = "Invalidate all events on cycle for pallet " + pallet.ToString(),
            EndOfRoute = false
          };
          newMsg.ProgramDetails["EditedCounters"] = string.Join(",", invalidatedCntrs);
          if (!string.IsNullOrEmpty(operatorName))
          {
            newMsg.ProgramDetails["operator"] = operatorName;
          }
          newLogEntries.Add(AddLogEntry(trans, newMsg, null, null));

          if (!string.IsNullOrEmpty(oldMatPutInQueue))
          {
            using (var checkMatInQueue = _connection.CreateCommand())
            {
              checkMatInQueue.CommandText = "SELECT Queue FROM queues WHERE MaterialID = $matid LIMIT 1";
              checkMatInQueue.Parameters.Add("matid", SqliteType.Integer);
              checkMatInQueue.Transaction = trans;

              foreach (var m in allMatIds)
              {
                checkMatInQueue.Parameters[0].Value = m;
                var currentQueue = checkMatInQueue.ExecuteScalar();
                if (currentQueue == null || currentQueue == DBNull.Value || (string)currentQueue != oldMatPutInQueue)
                {
                  newLogEntries.AddRange(AddToQueue(trans,
                    matId: m,
                    process: process - 1,
                    queue: oldMatPutInQueue,
                    position: -1,
                    operatorName: operatorName,
                    timeUTC: time,
                    reason: "InvalidateCycle"
                  ));
                }
              }
            }
          }

          trans.Commit();
        }
      }

      foreach (var l in newLogEntries) _cfg.OnNewLogEntry(l, null, this);

      return newLogEntries;
    }
    #endregion

    #region Material IDs
    public long AllocateMaterialID(string unique, string part, int numProc)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          var trans = _connection.BeginTransaction();
          cmd.Transaction = trans;
          try
          {
            cmd.CommandText = "INSERT INTO matdetails(UniqueStr, PartName, NumProcesses) VALUES ($uniq,$part,$numproc)";
            cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
            cmd.Parameters.Add("part", SqliteType.Text).Value = part;
            cmd.Parameters.Add("numproc", SqliteType.Integer).Value = numProc;
            cmd.ExecuteNonQuery();
            cmd.CommandText = "SELECT last_insert_rowid()";
            cmd.Parameters.Clear();
            var matID = (long)cmd.ExecuteScalar();
            trans.Commit();
            return matID;
          }
          catch
          {
            trans.Rollback();
            throw;
          }
        }
      }
    }

    public long AllocateMaterialIDForCasting(string casting)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          var trans = _connection.BeginTransaction();
          cmd.Transaction = trans;
          try
          {
            cmd.CommandText = "INSERT INTO matdetails(PartName, NumProcesses) VALUES ($casting,1)";
            cmd.Parameters.Add("casting", SqliteType.Text).Value = casting;
            cmd.ExecuteNonQuery();
            cmd.CommandText = "SELECT last_insert_rowid()";
            cmd.Parameters.Clear();
            var matID = (long)cmd.ExecuteScalar();
            trans.Commit();
            return matID;
          }
          catch
          {
            trans.Rollback();
            throw;
          }
        }
      }
    }

    public void SetDetailsForMaterialID(long matID, string unique, string part, int? numProc)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "UPDATE matdetails SET UniqueStr = coalesce($uniq, UniqueStr), PartName = coalesce($part, PartName), NumProcesses = coalesce($numproc, NumProcesses) WHERE MaterialID = $mat";
          cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique == null ? DBNull.Value : (object)unique;
          cmd.Parameters.Add("part", SqliteType.Text).Value = part == null ? DBNull.Value : (object)part;
          cmd.Parameters.Add("numproc", SqliteType.Integer).Value = numProc == null ? DBNull.Value : (object)numProc;
          cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;
          cmd.ExecuteNonQuery();
        }
      }
    }

    public void RecordPathForProcess(long matID, int process, int path)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "INSERT OR REPLACE INTO mat_path_details(MaterialID, Process, Path) VALUES ($mid, $proc, $path)";
          cmd.Parameters.Add("mid", SqliteType.Integer).Value = matID;
          cmd.Parameters.Add("proc", SqliteType.Integer).Value = process;
          cmd.Parameters.Add("path", SqliteType.Integer).Value = path;
          cmd.ExecuteNonQuery();
        }
      }
    }

    public void CreateMaterialID(long matID, string unique, string part, int numProc)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "INSERT INTO matdetails(MaterialID, UniqueStr, PartName, NumProcesses) VALUES ($mid, $uniq, $part, $numproc)";
          cmd.Parameters.Add("mid", SqliteType.Integer).Value = matID;
          cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
          cmd.Parameters.Add("part", SqliteType.Text).Value = part;
          cmd.Parameters.Add("numproc", SqliteType.Integer).Value = numProc;
          cmd.ExecuteNonQuery();
        }
      }
    }

    public MachineWatchInterface.MaterialDetails GetMaterialDetails(long matID)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          var ret = GetMaterialDetails(matID, trans);
          trans.Commit();
          return ret;
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    private MachineWatchInterface.MaterialDetails GetMaterialDetails(long matID, IDbTransaction trans)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "SELECT UniqueStr, PartName, NumProcesses, Workorder, Serial FROM matdetails WHERE MaterialID = $mat";
        cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;

        MachineWatchInterface.MaterialDetails ret = null;
        using (var reader = cmd.ExecuteReader())
        {
          if (reader.Read())
          {
            ret = new MachineWatchInterface.MaterialDetails() { MaterialID = matID };
            if (!reader.IsDBNull(0)) ret.JobUnique = reader.GetString(0);
            if (!reader.IsDBNull(1)) ret.PartName = reader.GetString(1);
            if (!reader.IsDBNull(2)) ret.NumProcesses = reader.GetInt32(2);
            if (!reader.IsDBNull(3)) ret.Workorder = reader.GetString(3);
            if (!reader.IsDBNull(4)) ret.Serial = reader.GetString(4);
          }
        }

        if (ret != null)
        {
          ret.Paths = new Dictionary<int, int>();
          cmd.CommandText = "SELECT Process, Path FROM mat_path_details WHERE MaterialID = $mat";
          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              var proc = reader.GetInt32(0);
              var path = reader.GetInt32(1);
              ret.Paths[proc] = path;
            }
          }
        }

        return ret;
      }
    }

    public IReadOnlyList<MachineWatchInterface.MaterialDetails> GetMaterialDetailsForSerial(string serial)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          using (var cmd = _connection.CreateCommand())
          using (var cmd2 = _connection.CreateCommand())
          {
            ((IDbCommand)cmd).Transaction = trans;
            ((IDbCommand)cmd2).Transaction = trans;
            cmd.CommandText = "SELECT MaterialID, UniqueStr, PartName, NumProcesses, Workorder FROM matdetails WHERE Serial = $ser";
            cmd.Parameters.Add("ser", SqliteType.Text).Value = serial;

            cmd2.CommandText = "SELECT Process, Path FROM mat_path_details WHERE MaterialID = $mat";
            cmd2.Parameters.Add("mat", SqliteType.Integer);

            var ret = new List<MachineWatchInterface.MaterialDetails>();
            using (var reader = cmd.ExecuteReader())
            {
              while (reader.Read())
              {
                var mat = new MachineWatchInterface.MaterialDetails() { Serial = serial };
                if (!reader.IsDBNull(0)) mat.MaterialID = reader.GetInt64(0);
                if (!reader.IsDBNull(1)) mat.JobUnique = reader.GetString(1);
                if (!reader.IsDBNull(2)) mat.PartName = reader.GetString(2);
                if (!reader.IsDBNull(3)) mat.NumProcesses = reader.GetInt32(3);
                if (!reader.IsDBNull(4)) mat.Workorder = reader.GetString(4);
                ret.Add(mat);
              }
            }

            foreach (var mat in ret)
            {
              cmd2.Parameters[0].Value = mat.MaterialID;
              mat.Paths = new Dictionary<int, int>();
              using (var reader = cmd2.ExecuteReader())
              {
                while (reader.Read())
                {
                  var proc = reader.GetInt32(0);
                  var path = reader.GetInt32(1);
                  mat.Paths[proc] = path;
                }
              }
            }

            trans.Commit();
            return ret;
          }
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public List<MachineWatchInterface.MaterialDetails> GetMaterialForWorkorder(string workorder)
    {
      using (var trans = _connection.BeginTransaction())
      using (var cmd = _connection.CreateCommand())
      using (var pathCmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "SELECT MaterialID, UniqueStr, PartName, NumProcesses, Serial FROM matdetails WHERE Workorder IS NOT NULL AND Workorder = $work";
        cmd.Parameters.Add("work", SqliteType.Text).Value = workorder;

        var ret = new List<MachineWatchInterface.MaterialDetails>();
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var mat = new MachineWatchInterface.MaterialDetails() { MaterialID = reader.GetInt64(0), Workorder = workorder };
            if (!reader.IsDBNull(1)) mat.JobUnique = reader.GetString(1);
            if (!reader.IsDBNull(2)) mat.PartName = reader.GetString(2);
            if (!reader.IsDBNull(3)) mat.NumProcesses = reader.GetInt32(3);
            if (!reader.IsDBNull(4)) mat.Serial = reader.GetString(4);
            ret.Add(mat);
          }
        }

        pathCmd.CommandText = "SELECT Process, Path FROM mat_path_details WHERE MaterialID = $mat";
        pathCmd.Transaction = trans;
        var param = pathCmd.Parameters.Add("mat", SqliteType.Integer);
        foreach (var mat in ret)
        {
          mat.Paths = new Dictionary<int, int>();
          param.Value = mat.MaterialID;
          using (var reader = pathCmd.ExecuteReader())
          {
            while (reader.Read())
            {
              var proc = reader.GetInt32(0);
              var path = reader.GetInt32(1);
              mat.Paths[proc] = path;
            }
          }
        }

        trans.Commit();
        return ret;
      }
    }

    public List<MachineWatchInterface.MaterialDetails> GetMaterialForJobUnique(string jobUnique)
    {
      using (var trans = _connection.BeginTransaction())
      using (var cmd = _connection.CreateCommand())
      using (var pathCmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "SELECT MaterialID, PartName, NumProcesses, Serial, Workorder FROM matdetails WHERE UniqueStr = $uniq ORDER BY Workorder, Serial";
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = jobUnique;

        var ret = new List<MachineWatchInterface.MaterialDetails>();
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var mat = new MachineWatchInterface.MaterialDetails() { MaterialID = reader.GetInt64(0), JobUnique = jobUnique };
            if (!reader.IsDBNull(1)) mat.PartName = reader.GetString(1);
            if (!reader.IsDBNull(2)) mat.NumProcesses = reader.GetInt32(2);
            if (!reader.IsDBNull(3)) mat.Serial = reader.GetString(3);
            if (!reader.IsDBNull(4)) mat.Workorder = reader.GetString(4);
            ret.Add(mat);
          }
        }

        pathCmd.CommandText = "SELECT Process, Path FROM mat_path_details WHERE MaterialID = $mat";
        pathCmd.Transaction = trans;
        var param = pathCmd.Parameters.Add("mat", SqliteType.Integer);
        foreach (var mat in ret)
        {
          mat.Paths = new Dictionary<int, int>();
          param.Value = mat.MaterialID;
          using (var reader = pathCmd.ExecuteReader())
          {
            while (reader.Read())
            {
              var proc = reader.GetInt32(0);
              var path = reader.GetInt32(1);
              mat.Paths[proc] = path;
            }
          }
        }

        trans.Commit();
        return ret;
      }
    }

    private void RecordSerialForMaterialID(IDbTransaction trans, long matID, string serial)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "UPDATE matdetails SET Serial = $ser WHERE MaterialID = $mat";
        if (string.IsNullOrEmpty(serial))
          cmd.Parameters.Add("ser", SqliteType.Text).Value = DBNull.Value;
        else
          cmd.Parameters.Add("ser", SqliteType.Text).Value = serial;
        cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;
        cmd.ExecuteNonQuery();
      }
    }

    private void RecordWorkorderForMaterialID(IDbTransaction trans, long matID, string workorder)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "UPDATE matdetails SET Workorder = $work WHERE MaterialID = $mat";
        if (string.IsNullOrEmpty(workorder))
          cmd.Parameters.Add("work", SqliteType.Text).Value = DBNull.Value;
        else
          cmd.Parameters.Add("work", SqliteType.Text).Value = workorder;
        cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;
        cmd.ExecuteNonQuery();
      }

    }
    #endregion

    #region Queues

    private IReadOnlyList<MachineWatchInterface.LogEntry> AddToQueue(IDbTransaction trans, long matId, int process, string queue, int position, string operatorName, DateTime timeUTC, string reason)
    {
      var mat = new EventLogMaterial()
      {
        MaterialID = matId,
        Process = process,
        Face = ""
      };

      return AddToQueue(trans, mat, queue, position, operatorName, timeUTC, reason);
    }

    private IReadOnlyList<MachineWatchInterface.LogEntry> AddToQueue(IDbTransaction trans, EventLogMaterial mat, string queue, int position, string operatorName, DateTime timeUTC, string reason)
    {
      var ret = new List<MachineWatchInterface.LogEntry>();

      ret.AddRange(RemoveFromAllQueues(trans, mat, operatorName, timeUTC));

      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        if (position >= 0)
        {
          cmd.CommandText = "UPDATE queues SET Position = Position + 1 " +
                              " WHERE Queue = $q AND Position >= $p";
          cmd.Parameters.Add("q", SqliteType.Text).Value = queue;
          cmd.Parameters.Add("p", SqliteType.Integer).Value = position;
          cmd.ExecuteNonQuery();

          cmd.CommandText =
              "INSERT INTO queues(MaterialID, Queue, Position, AddTimeUTC) " +
              " VALUES ($m, $q, (SELECT MIN(IFNULL(MAX(Position) + 1, 0), $p) FROM queues WHERE Queue = $q), $t)";
          cmd.Parameters.Add("m", SqliteType.Integer).Value = mat.MaterialID;
          cmd.Parameters.Add("t", SqliteType.Integer).Value = timeUTC.Ticks;
          cmd.ExecuteNonQuery();
        }
        else
        {
          cmd.CommandText =
              "INSERT INTO queues(MaterialID, Queue, Position, AddTimeUTC) " +
              " VALUES ($m, $q, (SELECT IFNULL(MAX(Position) + 1, 0) FROM queues WHERE Queue = $q), $t)";
          cmd.Parameters.Add("m", SqliteType.Integer).Value = mat.MaterialID;
          cmd.Parameters.Add("q", SqliteType.Text).Value = queue;
          cmd.Parameters.Add("t", SqliteType.Integer).Value = timeUTC.Ticks;
          cmd.ExecuteNonQuery();
        }
      }

      int resultingPosition;
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText = "SELECT Position FROM queues WHERE Queue = $q AND MaterialID = $m";
        cmd.Parameters.Add("m", SqliteType.Integer).Value = mat.MaterialID;
        cmd.Parameters.Add("q", SqliteType.Text).Value = queue;
        resultingPosition = Convert.ToInt32(cmd.ExecuteScalar());
      }

      var log = new NewEventLogEntry()
      {
        Material = new[] { mat },
        Pallet = "",
        LogType = MachineWatchInterface.LogType.AddToQueue,
        LocationName = queue,
        LocationNum = resultingPosition,
        Program = reason ?? "",
        StartOfCycle = false,
        EndTimeUTC = timeUTC,
        Result = "",
        EndOfRoute = false
      };
      if (!string.IsNullOrEmpty(operatorName))
      {
        log.ProgramDetails["operator"] = operatorName;
      }

      ret.Add(AddLogEntry(trans, log, null, null));

      return ret;
    }

    private IReadOnlyList<MachineWatchInterface.LogEntry> RemoveFromAllQueues(IDbTransaction trans, long matID, int process, string operatorName, DateTime timeUTC)
    {
      var mat = new EventLogMaterial()
      {
        MaterialID = matID,
        Process = process,
        Face = ""
      };

      return RemoveFromAllQueues(trans, mat, operatorName, timeUTC);
    }

    private IReadOnlyList<MachineWatchInterface.LogEntry> RemoveFromAllQueues(IDbTransaction trans, EventLogMaterial mat, string operatorName, DateTime timeUTC)
    {
      using (var findCmd = _connection.CreateCommand())
      using (var updatePosCmd = _connection.CreateCommand())
      using (var deleteCmd = _connection.CreateCommand())
      {
        ((IDbCommand)findCmd).Transaction = trans;
        findCmd.CommandText = "SELECT Queue, Position, AddTimeUTC FROM queues WHERE MaterialID = $mid";
        findCmd.Parameters.Add("mid", SqliteType.Integer).Value = mat.MaterialID;

        ((IDbCommand)updatePosCmd).Transaction = trans;
        updatePosCmd.CommandText =
            "UPDATE queues SET Position = Position - 1 " +
            " WHERE Queue = $q AND Position > $pos";
        updatePosCmd.Parameters.Add("q", SqliteType.Text);
        updatePosCmd.Parameters.Add("pos", SqliteType.Integer);

        ((IDbCommand)deleteCmd).Transaction = trans;
        deleteCmd.CommandText = "DELETE FROM queues WHERE MaterialID = $mid";
        deleteCmd.Parameters.Add("mid", SqliteType.Integer).Value = mat.MaterialID;

        var logs = new List<MachineWatchInterface.LogEntry>();

        using (var reader = findCmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var queue = reader.GetString(0);
            var pos = reader.GetInt32(1);
            var addTime = reader.IsDBNull(2) ? null : (DateTime?)(new DateTime(reader.GetInt64(2), DateTimeKind.Utc));

            var log = new NewEventLogEntry()
            {
              Material = new[] { mat },
              Pallet = "",
              LogType = MachineWatchInterface.LogType.RemoveFromQueue,
              LocationName = queue,
              LocationNum = pos,
              Program = "",
              StartOfCycle = false,
              EndTimeUTC = timeUTC,
              Result = "",
              EndOfRoute = false,
              ElapsedTime = addTime.HasValue ? timeUTC.Subtract(addTime.Value) : TimeSpan.Zero
            };
            if (!string.IsNullOrEmpty(operatorName))
            {
              log.ProgramDetails["operator"] = operatorName;
            }

            logs.Add(AddLogEntry(trans, log, null, null));

            updatePosCmd.Parameters[0].Value = queue;
            updatePosCmd.Parameters[1].Value = pos;
            updatePosCmd.ExecuteNonQuery();
          }
        }

        deleteCmd.ExecuteNonQuery();

        return logs;
      }
    }

    /// Find parts without an assigned unique in the queue, and assign them to the given unique
    public IReadOnlyList<long> AllocateCastingsInQueue(string queue, string casting, string unique, string part, int proc1Path, int numProcesses, int count)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        var matIds = new List<long>();
        try
        {
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;

            cmd.CommandText = "SELECT queues.MaterialID FROM queues " +
                " INNER JOIN matdetails ON queues.MaterialID = matdetails.MaterialID " +
                " WHERE Queue = $q AND matdetails.PartName = $c AND matdetails.UniqueStr IS NULL " +
                " ORDER BY Position ASC" +
                " LIMIT $cnt ";
            cmd.Parameters.Add("q", SqliteType.Text).Value = queue;
            cmd.Parameters.Add("c", SqliteType.Text).Value = casting;
            cmd.Parameters.Add("cnt", SqliteType.Integer).Value = count;
            using (var reader = cmd.ExecuteReader())
            {
              while (reader.Read()) matIds.Add(reader.GetInt64(0));
            }

            if (matIds.Count != count)
            {
              trans.Rollback();
              return new List<long>();
            }

            cmd.CommandText = "UPDATE matdetails SET UniqueStr = $uniq, PartName = $p, NumProcesses = $numproc WHERE MaterialID = $mid";
            cmd.Parameters.Clear();
            cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
            cmd.Parameters.Add("p", SqliteType.Text).Value = part;
            cmd.Parameters.Add("numproc", SqliteType.Integer).Value = numProcesses;
            cmd.Parameters.Add("mid", SqliteType.Integer);

            foreach (var matId in matIds)
            {
              cmd.Parameters[3].Value = matId;
              cmd.ExecuteNonQuery();
            }

            cmd.CommandText = "INSERT OR REPLACE INTO mat_path_details(MaterialID, Process, Path) VALUES ($mid, 1, $path)";
            cmd.Parameters.Clear();
            cmd.Parameters.Add("mid", SqliteType.Integer);
            cmd.Parameters.Add("path", SqliteType.Integer).Value = proc1Path;
            foreach (var matId in matIds)
            {
              cmd.Parameters[0].Value = matId;
              cmd.ExecuteNonQuery();
            }
          }
          trans.Commit();
          return matIds;
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public void MarkCastingsAsUnallocated(IEnumerable<long> matIds, string casting)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;

            cmd.CommandText = "UPDATE matdetails SET UniqueStr = NULL, PartName = $c WHERE MaterialID = $mid";
            cmd.Parameters.Clear();
            cmd.Parameters.Add("mid", SqliteType.Integer);
            cmd.Parameters.Add("c", SqliteType.Text).Value = casting;

            foreach (var matId in matIds)
            {
              cmd.Parameters[0].Value = matId;
              cmd.ExecuteNonQuery();
            }

            cmd.CommandText = "DELETE FROM mat_path_details WHERE MaterialID = $mid";
            cmd.Parameters.Clear();
            cmd.Parameters.Add("mid", SqliteType.Integer);

            foreach (var matId in matIds)
            {
              cmd.Parameters[0].Value = matId;
              cmd.ExecuteNonQuery();
            }
          }
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }

    }

    public struct QueuedMaterial
    {
      public long MaterialID { get; set; }
      public string Queue { get; set; }
      public int Position { get; set; }
      public string Unique { get; set; }
      public string PartNameOrCasting { get; set; }
      public int NumProcesses { get; set; }
      public DateTime? AddTimeUTC { get; set; }
    }

    public IEnumerable<QueuedMaterial> GetMaterialInQueue(string queue)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        var ret = new List<QueuedMaterial>();
        try
        {
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;
            cmd.CommandText = "SELECT queues.MaterialID, Position, UniqueStr, PartName, NumProcesses, AddTimeUTC " +
              " FROM queues " +
              " LEFT OUTER JOIN matdetails ON queues.MaterialID = matdetails.MaterialID " +
              " WHERE Queue = $q " +
              " ORDER BY Position";
            cmd.Parameters.Add("q", SqliteType.Text).Value = queue;
            using (var reader = cmd.ExecuteReader())
            {
              while (reader.Read())
              {
                ret.Add(new QueuedMaterial()
                {
                  MaterialID = reader.GetInt64(0),
                  Queue = queue,
                  Position = reader.GetInt32(1),
                  Unique = reader.IsDBNull(2) ? "" : reader.GetString(2),
                  PartNameOrCasting = reader.IsDBNull(3) ? "" : reader.GetString(3),
                  NumProcesses = reader.IsDBNull(4) ? 1 : reader.GetInt32(4),
                  AddTimeUTC = reader.IsDBNull(5) ? null : ((DateTime?)(new DateTime(reader.GetInt64(5), DateTimeKind.Utc))),
                });
              }
            }
            trans.Commit();
          }
          return ret;
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public IEnumerable<QueuedMaterial> GetMaterialInAllQueues()
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        var ret = new List<QueuedMaterial>();
        try
        {
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;
            cmd.CommandText = "SELECT queues.MaterialID, Queue, Position, UniqueStr, PartName, NumProcesses, AddTimeUTC " +
              " FROM queues " +
              " LEFT OUTER JOIN matdetails ON queues.MaterialID = matdetails.MaterialID " +
              " ORDER BY Queue, Position";
            using (var reader = cmd.ExecuteReader())
            {
              while (reader.Read())
              {
                ret.Add(new QueuedMaterial()
                {
                  MaterialID = reader.GetInt64(0),
                  Queue = reader.GetString(1),
                  Position = reader.GetInt32(2),
                  Unique = reader.IsDBNull(3) ? "" : reader.GetString(3),
                  PartNameOrCasting = reader.IsDBNull(4) ? "" : reader.GetString(4),
                  NumProcesses = reader.IsDBNull(5) ? 1 : reader.GetInt32(5),
                  AddTimeUTC = reader.IsDBNull(6) ? null : ((DateTime?)(new DateTime(reader.GetInt64(6), DateTimeKind.Utc))),
                });
              }
            }
            trans.Commit();
            return ret;
          }
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public int? NextProcessForQueuedMaterial(long matId)
    {
      lock (_cfg)
      {
        using (var loadCmd = _connection.CreateCommand())
        {
          loadCmd.CommandText = "SELECT MAX(m.Process) FROM " +
            " stations_mat m " +
            " WHERE m.MaterialID = $matid AND " +
            "   NOT EXISTS (" +
            "    SELECT 1 FROM stations s, program_details d " +
            "      WHERE s.Counter = m.Counter AND s.Counter = d.Counter AND d.Key = 'PalletCycleInvalidated'" +
            "   ) AND " +
            "   EXISTS (" +
            "    SELECT 1 FROM stations s WHERE s.Counter = m.Counter AND s.StationLoc IN (" + LogTypesToCheckForNextProcess + ")" +
            "   )";
          loadCmd.Parameters.Add("matid", SqliteType.Integer).Value = matId;

          var val = loadCmd.ExecuteScalar();
          if (val != null && val != DBNull.Value)
          {
            return Convert.ToInt32(val) + 1;
          }
          else
          {
            return null;
          }
        }
      }
    }
    #endregion

    #region Pending Loads

    public void AddPendingLoad(string pal, string key, int load, TimeSpan elapsed, TimeSpan active, string foreignID)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();

        try
        {
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;

            cmd.CommandText = "INSERT INTO pendingloads(Pallet, Key, LoadStation, Elapsed, ActiveTime, ForeignID)" +
                "VALUES ($pal,$key,$load,$elapsed,$active,$foreign)";

            cmd.Parameters.Add("pal", SqliteType.Text).Value = pal;
            cmd.Parameters.Add("key", SqliteType.Text).Value = key;
            cmd.Parameters.Add("load", SqliteType.Integer).Value = load;
            cmd.Parameters.Add("elapsed", SqliteType.Integer).Value = elapsed.Ticks;
            cmd.Parameters.Add("active", SqliteType.Integer).Value = active.Ticks;
            if (string.IsNullOrEmpty(foreignID))
              cmd.Parameters.Add("foreign", SqliteType.Text).Value = DBNull.Value;
            else
              cmd.Parameters.Add("foreign", SqliteType.Text).Value = foreignID;

            cmd.ExecuteNonQuery();

            trans.Commit();
          }
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public struct PendingLoad
    {
      public string Pallet;
      public string Key;
      public int LoadStation;
      public TimeSpan Elapsed;
      public TimeSpan ActiveOperationTime;
      public string ForeignID;
    }

    public List<PendingLoad> PendingLoads(string pallet)
    {
      lock (_cfg)
      {
        var ret = new List<PendingLoad>();

        var trans = _connection.BeginTransaction();
        try
        {
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;

            cmd.CommandText = "SELECT Key, LoadStation, Elapsed, ActiveTime, ForeignID FROM pendingloads WHERE Pallet = $pal";
            cmd.Parameters.Add("pal", SqliteType.Text).Value = pallet;

            using (var reader = cmd.ExecuteReader())
            {
              while (reader.Read())
              {
                var p = default(PendingLoad);
                p.Pallet = pallet;
                p.Key = reader.GetString(0);
                p.LoadStation = reader.GetInt32(1);
                p.Elapsed = new TimeSpan(reader.GetInt64(2));
                if (!reader.IsDBNull(3))
                  p.ActiveOperationTime = TimeSpan.FromTicks(reader.GetInt64(3));
                if (reader.IsDBNull(4))
                  p.ForeignID = null;
                else
                  p.ForeignID = reader.GetString(4);
                ret.Add(p);
              }
            }

            trans.Commit();
          }
        }
        catch
        {
          trans.Rollback();
          throw;
        }

        return ret;
      }
    }

    public List<PendingLoad> AllPendingLoads()
    {
      lock (_cfg)
      {
        var ret = new List<PendingLoad>();

        var trans = _connection.BeginTransaction();
        try
        {
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;

            cmd.CommandText = "SELECT Key, LoadStation, Elapsed, ActiveTime, ForeignID, Pallet FROM pendingloads";

            using (var reader = cmd.ExecuteReader())
            {
              while (reader.Read())
              {
                var p = default(PendingLoad);
                p.Key = reader.GetString(0);
                p.LoadStation = reader.GetInt32(1);
                p.Elapsed = new TimeSpan(reader.GetInt64(2));
                if (!reader.IsDBNull(3))
                  p.ActiveOperationTime = TimeSpan.FromTicks(reader.GetInt64(3));
                if (reader.IsDBNull(4))
                  p.ForeignID = null;
                else
                  p.ForeignID = reader.GetString(4);
                p.Pallet = reader.GetString(5);
                ret.Add(p);
              }
            }

            trans.Commit();
          }
        }
        catch
        {
          trans.Rollback();
          throw;
        }

        return ret;
      }
    }

    public void CompletePalletCycle(string pal, DateTime timeUTC, string foreignID)
    {
      CompletePalletCycle(pal, timeUTC, foreignID, null, generateSerials: false);
    }

    public void CompletePalletCycle(string pal, DateTime timeUTC, string foreignID,
                                    IDictionary<string, IEnumerable<EventLogMaterial>> mat, bool generateSerials)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();

        var newEvts = new List<BlackMaple.MachineWatchInterface.LogEntry>();
        try
        {

          using (var lastTimeCmd = _connection.CreateCommand())
          {
            lastTimeCmd.CommandText = "SELECT TimeUTC FROM stations where Pallet = $pal AND Result = 'PalletCycle' " +
                                    "ORDER BY Counter DESC LIMIT 1";
            lastTimeCmd.Parameters.Add("pal", SqliteType.Text).Value = pal;

            var elapsedTime = TimeSpan.Zero;
            var lastCycleTime = lastTimeCmd.ExecuteScalar();
            if (lastCycleTime != null && lastCycleTime != DBNull.Value)
              elapsedTime = timeUTC.Subtract(new DateTime((long)lastCycleTime, DateTimeKind.Utc));

            if (lastCycleTime == null || lastCycleTime == DBNull.Value || elapsedTime != TimeSpan.Zero)
            {
              newEvts.Add(AddLogEntry(trans, new NewEventLogEntry()
              {
                Material = new EventLogMaterial[] { },
                Pallet = pal,
                LogType = BlackMaple.MachineWatchInterface.LogType.PalletCycle,
                LocationName = "Pallet Cycle",
                LocationNum = 1,
                Program = "",
                StartOfCycle = false,
                EndTimeUTC = timeUTC,
                Result = "PalletCycle",
                EndOfRoute = false,
                ElapsedTime = elapsedTime,
                ActiveOperationTime = TimeSpan.Zero
              }, foreignID, null));
            }

            if (mat == null)
            {
              trans.Commit();
              foreach (var e in newEvts)
                _cfg.OnNewLogEntry(e, foreignID, this);
              return;
            }

            // Copy over pending loads
            using (var loadPending = _connection.CreateCommand())
            {
              loadPending.Transaction = trans;
              loadPending.CommandText = "SELECT Key, LoadStation, Elapsed, ActiveTime, ForeignID FROM pendingloads WHERE Pallet = $pal";
              loadPending.Parameters.Add("pal", SqliteType.Text).Value = pal;

              using (var reader = loadPending.ExecuteReader())
              {
                while (reader.Read())
                {
                  var key = reader.GetString(0);
                  if (mat.ContainsKey(key))
                  {
                    if (generateSerials && _cfg.Settings.SerialType == SerialType.AssignOneSerialPerCycle)
                    {

                      // find a material id to use to create the serial
                      long matID = -1;
                      foreach (var m in mat[key])
                      {
                        if (m.MaterialID >= 0)
                        {
                          matID = m.MaterialID;
                          break;
                        }
                      }
                      if (matID >= 0)
                      {
                        using (var checkConn = _connection.CreateCommand())
                        {
                          checkConn.Transaction = trans;
                          checkConn.CommandText = "SELECT Serial FROM matdetails WHERE MaterialID = $mid LIMIT 1";
                          checkConn.Parameters.Add("mid", SqliteType.Integer).Value = matID;
                          var existingSerial = checkConn.ExecuteScalar();
                          if (existingSerial != null
                              && existingSerial != DBNull.Value
                              && !string.IsNullOrEmpty(existingSerial.ToString())
                             )
                          {
                            //already has an assigned serial, skip assignment
                            matID = -1;

                          }
                        }
                      }
                      if (matID >= 0)
                      {
                        var serial = _cfg.Settings.ConvertMaterialIDToSerial(matID);
                        serial = serial.Substring(0, Math.Min(_cfg.Settings.SerialLength, serial.Length));
                        serial = serial.PadLeft(_cfg.Settings.SerialLength, '0');
                        // add the serial
                        foreach (var m in mat[key])
                        {
                          if (m.MaterialID >= 0)
                            RecordSerialForMaterialID(trans, m.MaterialID, serial);
                        }
                        newEvts.Add(AddLogEntry(trans, new NewEventLogEntry()
                        {
                          Material = mat[key],
                          Pallet = "",
                          LogType = MachineWatchInterface.LogType.PartMark,
                          LocationName = "Mark",
                          LocationNum = 1,
                          Program = "MARK",
                          StartOfCycle = false,
                          EndTimeUTC = timeUTC.AddSeconds(2),
                          Result = serial,
                          EndOfRoute = false
                        }, null, null));
                      }

                    }
                    else if (generateSerials && _cfg.Settings.SerialType == SerialType.AssignOneSerialPerMaterial)
                    {
                      using (var checkConn = _connection.CreateCommand())
                      {
                        checkConn.Transaction = trans;
                        checkConn.CommandText = "SELECT Serial FROM matdetails WHERE MaterialID = $mid LIMIT 1";
                        checkConn.Parameters.Add("mid", SqliteType.Integer);
                        foreach (var m in mat[key])
                        {
                          checkConn.Parameters[0].Value = m.MaterialID;
                          var existingSerial = checkConn.ExecuteScalar();
                          if (existingSerial != null
                              && existingSerial != DBNull.Value
                              && !string.IsNullOrEmpty(existingSerial.ToString())
                              )
                          {
                            //already has an assigned serial, skip assignment
                            continue;
                          }

                          var serial = _cfg.Settings.ConvertMaterialIDToSerial(m.MaterialID);
                          serial = serial.Substring(0, Math.Min(_cfg.Settings.SerialLength, serial.Length));
                          serial = serial.PadLeft(_cfg.Settings.SerialLength, '0');
                          if (m.MaterialID < 0) continue;
                          RecordSerialForMaterialID(trans, m.MaterialID, serial);
                          newEvts.Add(AddLogEntry(trans, new NewEventLogEntry()
                          {
                            Material = new[] { m },
                            Pallet = "",
                            LogType = MachineWatchInterface.LogType.PartMark,
                            LocationName = "Mark",
                            LocationNum = 1,
                            Program = "MARK",
                            StartOfCycle = false,
                            EndTimeUTC = timeUTC.AddSeconds(2),
                            Result = serial,
                            EndOfRoute = false
                          }, null, null));
                        }
                      }
                    }
                    newEvts.Add(AddLogEntry(trans, new NewEventLogEntry()
                    {
                      Material = mat[key],
                      Pallet = pal,
                      LogType = BlackMaple.MachineWatchInterface.LogType.LoadUnloadCycle,
                      LocationName = "L/U",
                      LocationNum = reader.GetInt32(1),
                      Program = "LOAD",
                      StartOfCycle = false,
                      EndTimeUTC = timeUTC.AddSeconds(1),
                      Result = "LOAD",
                      EndOfRoute = false,
                      ElapsedTime = TimeSpan.FromTicks(reader.GetInt64(2)),
                      ActiveOperationTime = reader.IsDBNull(3) ? TimeSpan.Zero : TimeSpan.FromTicks(reader.GetInt64(3))
                    },
                    foreignID: reader.IsDBNull(4) ? null : reader.GetString(4),
                    origMessage: null));

                    foreach (var logMat in mat[key])
                    {
                      var prevProcMat = new EventLogMaterial() { MaterialID = logMat.MaterialID, Process = logMat.Process - 1, Face = "" };
                      newEvts.AddRange(RemoveFromAllQueues(trans, prevProcMat, operatorName: null, timeUTC: timeUTC.AddSeconds(1)));
                    }

                  }
                }
              }
            }

            using (var delCmd = _connection.CreateCommand())
            {
              delCmd.Transaction = trans;
              delCmd.CommandText = "DELETE FROM pendingloads WHERE Pallet = $pal";
              delCmd.Parameters.Add("pal", SqliteType.Text).Value = pal;
              delCmd.ExecuteNonQuery();
            }

            trans.Commit();
          }
        }
        catch
        {
          trans.Rollback();
          throw;
        }

        foreach (var e in newEvts)
          _cfg.OnNewLogEntry(e, foreignID, this);
      }
    }

    #endregion

    #region Inspection Counts
    private static Random _rand = new Random();

    private MachineWatchInterface.InspectCount QueryCount(IDbTransaction trans, string counter, int maxVal)
    {
      var cnt = new MachineWatchInterface.InspectCount();
      cnt.Counter = counter;

      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "SELECT Val, LastUTC FROM inspection_counters WHERE Counter = $cntr";
        cmd.Parameters.Add("cntr", SqliteType.Text).Value = counter;

        using (IDataReader reader = cmd.ExecuteReader())
        {
          if (reader.Read())
          {
            cnt.Value = reader.GetInt32(0);
            if (reader.IsDBNull(1))
              cnt.LastUTC = DateTime.MaxValue;
            else
              cnt.LastUTC = new DateTime(reader.GetInt64(1), DateTimeKind.Utc);

          }
          else
          {
            if (maxVal <= 1)
              cnt.Value = 0;
            else
              cnt.Value = _rand.Next(0, maxVal - 1);

            cnt.LastUTC = DateTime.MaxValue;
          }
        }
      }

      return cnt;
    }

    public List<MachineWatchInterface.InspectCount> LoadInspectCounts()
    {
      lock (_cfg)
      {
        List<MachineWatchInterface.InspectCount> ret = new List<MachineWatchInterface.InspectCount>();

        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT Counter, Val, LastUTC FROM inspection_counters";

          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              var insp = default(MachineWatchInterface.InspectCount);
              insp.Counter = reader.GetString(0);
              insp.Value = reader.GetInt32(1);
              if (reader.IsDBNull(2))
                insp.LastUTC = DateTime.MaxValue;
              else
                insp.LastUTC = new DateTime(reader.GetInt64(2), DateTimeKind.Utc);
              ret.Add(insp);
            }
          }
        }

        return ret;
      }
    }

    private void SetInspectionCount(IDbTransaction trans, MachineWatchInterface.InspectCount cnt)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "INSERT OR REPLACE INTO inspection_counters(Counter,Val,LastUTC) VALUES ($cntr,$val,$time)";
        cmd.Parameters.Add("cntr", SqliteType.Text).Value = cnt.Counter;
        cmd.Parameters.Add("val", SqliteType.Integer).Value = cnt.Value;
        cmd.Parameters.Add("time", SqliteType.Integer).Value = cnt.LastUTC.Ticks;
        cmd.ExecuteNonQuery();
      }
    }

    public void SetInspectCounts(IEnumerable<MachineWatchInterface.InspectCount> counts)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "INSERT OR REPLACE INTO inspection_counters(Counter, Val, LastUTC) VALUES ($cntr,$val,$last)";
          cmd.Parameters.Add("cntr", SqliteType.Text);
          cmd.Parameters.Add("val", SqliteType.Integer);
          cmd.Parameters.Add("last", SqliteType.Integer);

          var trans = _connection.BeginTransaction();
          try
          {
            cmd.Transaction = trans;

            foreach (var insp in counts)
            {
              cmd.Parameters[0].Value = insp.Counter;
              cmd.Parameters[1].Value = insp.Value;
              cmd.Parameters[2].Value = insp.LastUTC.Ticks;
              cmd.ExecuteNonQuery();
            }

            trans.Commit();
          }
          catch
          {
            trans.Rollback();
            throw;
          }
        }
      }
    }
    #endregion

    #region Inspection Translation
    private Dictionary<int, MachineWatchInterface.MaterialProcessActualPath> LookupActualPath(IDbTransaction trans, long matID)
    {
      var byProc = new Dictionary<int, MachineWatchInterface.MaterialProcessActualPath>();
      MachineWatchInterface.MaterialProcessActualPath getPath(int proc)
      {
        if (byProc.ContainsKey(proc))
          return byProc[proc];
        else
        {
          var m = new MachineWatchInterface.MaterialProcessActualPath()
          {
            MaterialID = matID,
            Process = proc,
            Pallet = null,
            LoadStation = -1,
            UnloadStation = -1
          };
          byProc.Add(proc, m);
          return m;
        }
      }

      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "SELECT Pallet, StationLoc, StationName, StationNum, Process " +
            " FROM stations " +
            " INNER JOIN stations_mat ON stations.Counter = stations_mat.Counter " +
            " WHERE " +
            "    MaterialID = $mat AND Start = 0 " +
            "    AND (StationLoc = $ty1 OR StationLoc = $ty2) " +
            " ORDER BY stations.Counter ASC";
        cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;
        cmd.Parameters.Add("ty1", SqliteType.Integer).Value = (int)MachineWatchInterface.LogType.LoadUnloadCycle;
        cmd.Parameters.Add("ty2", SqliteType.Integer).Value = (int)MachineWatchInterface.LogType.MachineCycle;

        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            //for each log entry, we search for a matching route stop in the job
            //if we find one, we replace the counter in the program
            string pal = reader.GetString(0);
            var logTy = (MachineWatchInterface.LogType)reader.GetInt32(1);
            string statName = reader.GetString(2);
            int statNum = reader.GetInt32(3);
            int process = reader.GetInt32(4);

            var mat = getPath(process);

            if (!string.IsNullOrEmpty(pal))
              mat.Pallet = pal;

            switch (logTy)
            {
              case MachineWatchInterface.LogType.LoadUnloadCycle:
                if (mat.LoadStation == -1)
                  mat.LoadStation = statNum;
                else
                  mat.UnloadStation = statNum;
                break;

              case MachineWatchInterface.LogType.MachineCycle:
                mat.Stops.Add(new MachineWatchInterface.MaterialProcessActualPath.Stop()
                {
                  StationName = statName,
                  StationNum = statNum
                });
                break;
            }
          }
        }
      }

      return byProc;
    }

    private string TranslateInspectionCounter(long matID, Dictionary<int, MachineWatchInterface.MaterialProcessActualPath> actualPath, string counter)
    {
      foreach (var p in actualPath.Values)
      {
        counter = counter.Replace(
            MachineWatchInterface.JobInspectionData.PalletFormatFlag(p.Process),
            p.Pallet
        );
        counter = counter.Replace(
            MachineWatchInterface.JobInspectionData.LoadFormatFlag(p.Process),
            p.LoadStation.ToString()
        );
        counter = counter.Replace(
            MachineWatchInterface.JobInspectionData.UnloadFormatFlag(p.Process),
            p.UnloadStation.ToString()
        );
        for (int stopNum = 1; stopNum <= p.Stops.Count; stopNum++)
        {
          counter = counter.Replace(
              MachineWatchInterface.JobInspectionData.StationFormatFlag(p.Process, stopNum),
              p.Stops[stopNum - 1].StationNum.ToString()
          );
        }
      }
      return counter;
    }
    #endregion

    #region Inspection Decisions

    public class Decision
    {
      public long MaterialID;
      public string InspType;
      public string Counter;
      public bool Inspect;
      public bool Forced;
      public System.DateTime CreateUTC;
    }
    public IList<Decision> LookupInspectionDecisions(long matID)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          var ret = LookupInspectionDecisions(trans, matID);
          trans.Commit();
          return ret;
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }

    }

    private IList<Decision> LookupInspectionDecisions(IDbTransaction trans, long matID)
    {
      List<Decision> ret = new List<Decision>();
      using (var detailCmd = _connection.CreateCommand())
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "SELECT Counter, StationLoc, Program, TimeUTC, Result " +
            " FROM stations " +
            " WHERE " +
            "    Counter IN (SELECT Counter FROM stations_mat WHERE MaterialID = $mat) " +
            "    AND (StationLoc = $loc1 OR StationLoc = $loc2) " +
            " ORDER BY Counter ASC";
        cmd.Parameters.Add("$mat", SqliteType.Integer).Value = matID;
        cmd.Parameters.Add("$loc1", SqliteType.Integer).Value = MachineWatchInterface.LogType.InspectionForce;
        cmd.Parameters.Add("$loc2", SqliteType.Integer).Value = MachineWatchInterface.LogType.Inspection;

        ((IDbCommand)detailCmd).Transaction = trans;
        detailCmd.CommandText = "SELECT Value FROM program_details WHERE Counter = $cntr AND Key = 'InspectionType'";
        detailCmd.Parameters.Add("cntr", SqliteType.Integer);

        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var cntr = reader.GetInt64(0);
            var logTy = (MachineWatchInterface.LogType)reader.GetInt32(1);
            var prog = reader.GetString(2);
            var timeUtc = new DateTime(reader.GetInt64(3), DateTimeKind.Utc);
            var result = reader.GetString(4);
            var inspect = false;
            bool.TryParse(result, out inspect);

            if (logTy == MachineWatchInterface.LogType.Inspection)
            {
              detailCmd.Parameters[0].Value = cntr;
              var inspVal = detailCmd.ExecuteScalar();
              string inspType;
              if (inspVal != null)
              {
                inspType = inspVal.ToString();
              }
              else
              {
                // old code didn't record in details, so assume the counter is in a specific format
                var parts = prog.Split(',');
                if (parts.Length >= 2)
                  inspType = parts[1];
                else
                  inspType = "";
              }
              ret.Add(new Decision()
              {
                MaterialID = matID,
                InspType = inspType,
                Counter = prog,
                Inspect = inspect,
                Forced = false,
                CreateUTC = timeUtc
              });
            }
            else
            {
              ret.Add(new Decision()
              {
                MaterialID = matID,
                InspType = prog,
                Counter = "",
                Inspect = inspect,
                Forced = true,
                CreateUTC = timeUtc
              });
            }
          }
        }
      }
      return ret;
    }

    public IEnumerable<MachineWatchInterface.LogEntry> MakeInspectionDecisions(
        long matID,
        int process,
        IEnumerable<MachineWatchInterface.PathInspection> inspections,
        DateTime? mutcNow = null)
    {
      return AddEntryInTransaction(trans =>
          MakeInspectionDecisions(trans, matID, process, inspections, mutcNow)
      );
    }

    private List<MachineWatchInterface.LogEntry> MakeInspectionDecisions(
        IDbTransaction trans,
        long matID,
        int process,
        IEnumerable<MachineWatchInterface.PathInspection> inspections,
        DateTime? mutcNow)
    {
      var utcNow = mutcNow ?? DateTime.UtcNow;
      var logEntries = new List<MachineWatchInterface.LogEntry>();

      var actualPath = LookupActualPath(trans, matID);

      var decisions =
          LookupInspectionDecisions(trans, matID)
          .ToLookup(d => d.InspType, d => d);

      Dictionary<string, MachineWatchInterface.PathInspection> insps;
      if (inspections == null)
        insps = new Dictionary<string, MachineWatchInterface.PathInspection>();
      else
        insps = inspections.ToDictionary(x => x.InspectionType, x => x);


      var inspsToCheck = decisions.Select(x => x.Key).Union(insps.Keys).Distinct();
      foreach (var inspType in inspsToCheck)
      {
        bool inspect = false;
        string counter = "";
        bool alreadyRecorded = false;

        MachineWatchInterface.PathInspection iProg = null;
        if (insps.ContainsKey(inspType))
        {
          iProg = insps[inspType];
          counter = TranslateInspectionCounter(matID, actualPath, iProg.Counter);
        }


        if (decisions.Contains(inspType))
        {
          // use the decision
          foreach (var d in decisions[inspType])
          {
            inspect = inspect || d.Inspect;
            alreadyRecorded = alreadyRecorded || !d.Forced;
          }

        }

        if (!alreadyRecorded && iProg != null)
        {
          // use the counter
          var currentCount = QueryCount(trans, counter, iProg.MaxVal);
          if (iProg.MaxVal > 0)
          {
            currentCount.Value += 1;

            if (currentCount.Value >= iProg.MaxVal)
            {
              currentCount.Value = 0;
              inspect = true;
            }
          }
          else if (iProg.RandomFreq > 0)
          {
            if (_rand.NextDouble() < iProg.RandomFreq)
              inspect = true;
          }

          //now check lastutc
          if (iProg.TimeInterval > TimeSpan.Zero &&
              currentCount.LastUTC != DateTime.MaxValue &&
              currentCount.LastUTC.Add(iProg.TimeInterval) < utcNow)
          {
            inspect = true;
          }

          //update lastutc if there is an inspection
          if (inspect)
            currentCount.LastUTC = utcNow;

          //if no lastutc has been recoreded, record the current time.
          if (currentCount.LastUTC == DateTime.MaxValue)
            currentCount.LastUTC = utcNow;

          SetInspectionCount(trans, currentCount);
        }

        if (!alreadyRecorded)
        {
          var log = StoreInspectionDecision(trans,
              matID, process, actualPath, inspType, counter, utcNow, inspect);
          logEntries.Add(log);
        }
      }

      return logEntries;
    }

    private MachineWatchInterface.LogEntry StoreInspectionDecision(
        IDbTransaction trans,
        long matID, int proc, Dictionary<int, MachineWatchInterface.MaterialProcessActualPath> actualPath,
        string inspType, string counter, DateTime utcNow, bool inspect)
    {
      var mat =
          new EventLogMaterial() { MaterialID = matID, Process = proc, Face = "" };
      var pathSteps = actualPath.Values.OrderBy(p => p.Process).ToList();

      var log = new NewEventLogEntry()
      {
        Material = new[] { mat },
        Pallet = "",
        LogType = MachineWatchInterface.LogType.Inspection,
        LocationName = "Inspect",
        LocationNum = 1,
        Program = counter,
        StartOfCycle = false,
        EndTimeUTC = utcNow,
        Result = inspect.ToString(),
        EndOfRoute = false
      };

      log.ProgramDetails["InspectionType"] = inspType;
      log.ProgramDetails["ActualPath"] = Newtonsoft.Json.JsonConvert.SerializeObject(pathSteps);

      return AddLogEntry(trans, log, null, null);
    }

    #endregion

    #region Force and Next Piece Inspection
    public void ForceInspection(long matID, string inspType)
    {
      var mat = new EventLogMaterial() { MaterialID = matID, Process = 1, Face = "" };
      ForceInspection(mat, inspType, inspect: true, utcNow: DateTime.UtcNow);
    }

    public MachineWatchInterface.LogEntry ForceInspection(long materialID, int process, string inspType, bool inspect)
    {
      var mat = new EventLogMaterial() { MaterialID = materialID, Process = process, Face = "" };
      return ForceInspection(mat, inspType, inspect, DateTime.UtcNow);
    }

    public MachineWatchInterface.LogEntry ForceInspection(EventLogMaterial mat, string inspType, bool inspect)
    {
      return ForceInspection(mat, inspType, inspect, DateTime.UtcNow);
    }

    public MachineWatchInterface.LogEntry ForceInspection(
        EventLogMaterial mat, string inspType, bool inspect, DateTime utcNow)
    {
      return AddEntryInTransaction(trans =>
          RecordForceInspection(trans, mat, inspType, inspect, utcNow)
      );
    }

    private MachineWatchInterface.LogEntry RecordForceInspection(
        IDbTransaction trans,
        EventLogMaterial mat, string inspType, bool inspect, DateTime utcNow)
    {
      var log = new NewEventLogEntry()
      {
        Material = new[] { mat },
        Pallet = "",
        LogType = MachineWatchInterface.LogType.InspectionForce,
        LocationName = "Inspect",
        LocationNum = 1,
        Program = inspType,
        StartOfCycle = false,
        EndTimeUTC = utcNow,
        Result = inspect.ToString(),
        EndOfRoute = false
      };
      return AddLogEntry(trans, log, null, null);
    }

    public void NextPieceInspection(MachineWatchInterface.PalletLocation palLoc, string inspType)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {

          cmd.CommandText = "INSERT OR REPLACE INTO inspection_next_piece(StatType, StatNum, InspType)" +
              " VALUES ($loc,$locnum,$insp)";
          cmd.Parameters.Add("loc", SqliteType.Integer).Value = (int)palLoc.Location;
          cmd.Parameters.Add("locnum", SqliteType.Integer).Value = palLoc.Num;
          cmd.Parameters.Add("insp", SqliteType.Text).Value = inspType;

          cmd.ExecuteNonQuery();
        }
      }
    }

    public void CheckMaterialForNextPeiceInspection(MachineWatchInterface.PalletLocation palLoc, long matID)
    {
      var logs = new List<MachineWatchInterface.LogEntry>();

      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        using (var cmd2 = _connection.CreateCommand())
        {

          cmd.CommandText = "SELECT InspType FROM inspection_next_piece WHERE StatType = $loc AND StatNum = $locnum";
          cmd.Parameters.Add("loc", SqliteType.Integer).Value = (int)palLoc.Location;
          cmd.Parameters.Add("locnum", SqliteType.Integer).Value = palLoc.Num;

          var trans = _connection.BeginTransaction();
          try
          {
            cmd.Transaction = trans;

            IDataReader reader = cmd.ExecuteReader();
            try
            {
              var now = DateTime.UtcNow;
              while (reader.Read())
              {
                if (!reader.IsDBNull(0))
                {
                  var mat = new EventLogMaterial() { MaterialID = matID, Process = 1, Face = "" };
                  logs.Add(RecordForceInspection(trans, mat, reader.GetString(0), inspect: true, utcNow: now));
                }
              }

            }
            finally
            {
              reader.Close();
            }

            cmd.CommandText = "DELETE FROM inspection_next_piece WHERE StatType = $loc AND StatNum = $locnum";
            //keep the same parameters as above
            cmd.ExecuteNonQuery();

            trans.Commit();
          }
          catch
          {
            trans.Rollback();
            throw;
          }
        }

        foreach (var log in logs)
          _cfg.OnNewLogEntry(log, null, this);
      }
    }
    #endregion
  }
}
