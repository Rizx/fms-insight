using System;
using System.Data;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Makino
{
	public class StatusDB
	{
		#region Constructor and Init
		private SqliteConnection _connection;
		private object _lock;

		public StatusDB(string filename)
		{
			_lock = new object();
			if (System.IO.File.Exists(filename)) {
				_connection = BlackMaple.MachineFramework.SqliteExtensions.Connect(filename, newFile: false);
				_connection.Open();
				UpdateTables();
			}
			else {
				_connection = BlackMaple.MachineFramework.SqliteExtensions.Connect(filename, newFile: true);
				_connection.Open();
				try {
					CreateTables();
				}
				catch {
					_connection.Close();
					System.IO.File.Delete(filename);
					throw;
				}
			}
		}

		public StatusDB(SqliteConnection conn)
		{
			_lock = new object();
			_connection = conn;
		}

		public void Close()
		{
			_connection.Close();
		}
		#endregion

		#region Create/Update
		private const int Version = 1;

		public void CreateTables()
		{
			var cmd = _connection.CreateCommand();

			cmd.CommandText = "CREATE TABLE version(ver INTEGER)";
			cmd.ExecuteNonQuery();
			cmd.CommandText = "INSERT INTO version VALUES(" + Version.ToString() + ")";
			cmd.ExecuteNonQuery();

			cmd.CommandText = "CREATE TABLE matids(Pallet INTEGER, FixtureNum INTEGER, LoadedUTC INTEGER, LocCounter INTEGER, OrderName TEXT, MaterialID INTEGER, PRIMARY KEY(Pallet, FixtureNum, LoadedUTC, LocCounter))";
			cmd.ExecuteNonQuery();
			cmd.CommandText = "CREATE INDEX matids_idx ON matids(MaterialID)";
			cmd.ExecuteNonQuery();
		}

		private void UpdateTables()
		{
			var cmd = _connection.CreateCommand();

			cmd.CommandText = "SELECT ver FROM version";

			int curVersion = 0;

			using (var reader = cmd.ExecuteReader()) {
				if (reader.Read())
					curVersion = (int)reader.GetInt32(0);
				else
					curVersion = 0;
			}

			if (curVersion > Version)
				throw new ApplicationException("This input file was created with a newer version of Machine Watch.  Please upgrade Machine Watch");

			if (curVersion == Version) return;


			var trans = _connection.BeginTransaction();

			try {
				//add upgrade code here, in seperate functions
				//if (curVersion < 1) Ver0ToVer1(trans);

				//update the version in the database
				cmd.Transaction = trans;
				cmd.CommandText = "UPDATE version SET ver = " + Version.ToString();
				cmd.ExecuteNonQuery();

				trans.Commit();
			} catch {
				trans.Rollback();
				throw;
			}

			//only vacuum if we did some updating
			cmd.Transaction = null;
			cmd.CommandText = "VACUUM";
			cmd.ExecuteNonQuery();
		}

		public void SetFirstMaterialID(long matID)
		{
			using (var cmd = _connection.CreateCommand()) {
				cmd.CommandText = "INSERT INTO matids(Pallet, FixtureNum, LoadedUTC, LocCounter, OrderName, MaterialID)" +
					" VALUES(-1, -1, ?, -1, '', ?)";
				cmd.Parameters.Add("", SqliteType.Integer).Value = DateTime.MinValue.Ticks;
				cmd.Parameters.Add("", SqliteType.Integer).Value = matID;
				cmd.ExecuteNonQuery();
			}
		}
        #endregion

		#region MatIDs
		public struct MatIDRow
		{
			public DateTime LoadedUTC;
			public int LocCounter;
			public string Order;
			public long MatID;
		}

		public IList<MatIDRow> FindMaterialIDs(int pallet, int fixturenum, DateTime loadedUTC)
		{
			lock (_lock) {
				var trans = _connection.BeginTransaction();
				try {

					var ret = LoadMatIDs(pallet, fixturenum, loadedUTC, trans);

					trans.Commit();

					return ret;
				} catch {
					trans.Rollback();
					throw;
				}
			}
		}

		public IList<MatIDRow> CreateMaterialIDs(int pallet, int fixturenum, DateTime loadedUTC,
			string order, int numParts, int startingCounter)
		{
			lock (_lock) {
				var trans = _connection.BeginTransaction();
				try {

					var ret = AddMatIDs(pallet, fixturenum, loadedUTC, order, numParts, startingCounter, trans);

					trans.Commit();
					return ret;
				} catch {
					trans.Rollback();
					throw;
				}
			}
		}

		private List<MatIDRow> LoadMatIDs(int pallet, int fixturenum, DateTime beforeLoadedUTC, IDbTransaction trans)
		{
			var ret = new List<MatIDRow>();

			using (var cmd = _connection.CreateCommand()) {
				((IDbCommand)cmd).Transaction = trans;

				cmd.CommandText = "SELECT LoadedUTC, LocCounter, OrderName, MaterialID FROM " +
					"matids WHERE Pallet = ? AND FixtureNum = ? AND LoadedUTC <= ? " +
					"ORDER BY LoadedUTC DESC";
				cmd.Parameters.Add("", SqliteType.Integer).Value = pallet;
				cmd.Parameters.Add("", SqliteType.Integer).Value = fixturenum;
				cmd.Parameters.Add("", SqliteType.Integer).Value = beforeLoadedUTC.Ticks;

				DateTime lastTime = DateTime.MaxValue;

				using (IDataReader reader = cmd.ExecuteReader()) {
					while (reader.Read()) {

						//Only read a single LoadedUTC time
						var loadedUTC = new DateTime(reader.GetInt64(0), DateTimeKind.Utc);
						if (lastTime == DateTime.MaxValue)
							lastTime = loadedUTC;
						else if (lastTime != loadedUTC)
							break;

						var row = default(MatIDRow);
						row.LoadedUTC = loadedUTC;
						row.LocCounter = reader.GetInt32(1);
						row.Order = reader.GetString(2);
						row.MatID = reader.GetInt64(3);
						ret.Add(row);
					}
				}
			}

			return ret;
		}

		private List<MatIDRow> AddMatIDs(int pallet, int fixturenum, DateTime loadedUTC, string order,
			int numParts, int counterStart, IDbTransaction trans)
		{
			var ret = new List<MatIDRow>();

			using (var cmd = _connection.CreateCommand()) {
				((IDbCommand)cmd).Transaction = trans;

				cmd.CommandText = "SELECT MAX(MaterialID) FROM matids";
				var lastMatObj = cmd.ExecuteScalar();
				long lastMat;
				if (lastMatObj == null || lastMatObj == DBNull.Value)
					lastMat = 1;
				else
					lastMat = Convert.ToInt64(lastMatObj);

				cmd.CommandText = "INSERT INTO matids(Pallet, FixtureNum, LoadedUTC, LocCounter, OrderName, MaterialID)" +
					" VALUES (?,?,?,?,?,?)";
				cmd.Parameters.Add("", SqliteType.Integer).Value = pallet;
				cmd.Parameters.Add("", SqliteType.Integer).Value = fixturenum;
				cmd.Parameters.Add("", SqliteType.Integer).Value = loadedUTC.Ticks;
				cmd.Parameters.Add("", SqliteType.Integer);
				cmd.Parameters.Add("", SqliteType.Text).Value = order;
				cmd.Parameters.Add("", SqliteType.Integer);

				for (int i = 0; i < numParts; i++) {
					cmd.Parameters[3].Value = counterStart + i;
					cmd.Parameters[5].Value = lastMat + i + 1;
					cmd.ExecuteNonQuery();

					var row = default(MatIDRow);
					row.LoadedUTC = loadedUTC;
					row.LocCounter = counterStart + i;
					row.Order = order;
					row.MatID = lastMat + i + 1;
					ret.Add(row);
				}
			}

			return ret;
		}
        #endregion
	}
}

