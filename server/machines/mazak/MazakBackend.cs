using System;
using System.Diagnostics;
using System.Collections.Generic;
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface
{
	public class MazakBackend : IFMSBackend, IFMSImplementation
	{
		private DatabaseAccess database;
		private RoutingInfo routing;
		private HoldPattern hold;
		private LoadOperations loadOper;
		private LogTranslation logTrans;
		private ILogData logDataLoader;

		private JobLogDB jobLog;
		private InspectionDB insp;
		private JobDB jobDB;

		//Settings
		public DatabaseAccess.MazakDbType MazakType;
		public bool UseStartingOffsetForDueDate;
        public bool DecrementPriorityOnDownload;
		public bool CheckPalletsUsedOnce;

		public DatabaseAccess Database
		{
			get {
				return database;
			}
		}

		public LoadOperations LoadOperations
		{
			get { return loadOper; }
		}

		public JobLogDB JobLog {
			get { return jobLog; }
		}

		public LogTranslation LogTranslation {
			get { return logTrans; }
		}

    	FMSInfo IFMSImplementation.Info => new FMSInfo() {
			Name = "Mazak",
			Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString()
		};
    	IFMSBackend IFMSImplementation.Backend => this;
    	IList<IBackgroundWorker> IFMSImplementation.Workers => new List<IBackgroundWorker>();

    	public void Init(string dataDirectory, IConfig cfg, SerialSettings serSettings)
		{
            string localDbPath = cfg.GetValue<string>("Mazak", "Database Path");
            MazakType = DetectMazakType(cfg, localDbPath);

            // database settings
            string sqlConnectString = cfg.GetValue<string>("Mazak", "SQL ConnectionString");
            string dbConnStr;
            if (MazakType == DatabaseAccess.MazakDbType.MazakSmooth)
            {
                if (!string.IsNullOrEmpty(sqlConnectString))
                {
                    dbConnStr = sqlConnectString;
                }
                else if (!string.IsNullOrEmpty(localDbPath))
                {
                    // old installers put sql server computer name in localDbPath
                    dbConnStr = "Server=" + localDbPath + "\\pmcsqlserver;" +
                        "User ID=mazakpmc;Password=Fms-978";
                }
                else
                {
                    var b = new System.Data.SqlClient.SqlConnectionStringBuilder();
                    b.UserID = "mazakpmc";
                    b.Password = "Fms-978";
                    b.DataSource = "(local)";
                    dbConnStr = b.ConnectionString;
                }
            }
            else
            {
                dbConnStr = localDbPath;
                if (string.IsNullOrEmpty(dbConnStr))
                {
                    dbConnStr = "c:\\Mazak\\NFMS\\DB";
                }
            }

            // log csv
            string logPath = cfg.GetValue<string>("Mazak", "Log CSV Path");
            if (logPath == null || logPath == "")
                logPath = "c:\\Mazak\\FMS\\Log";

            // general config
            string useStartingForDue = cfg.GetValue<string>("Mazak", "Use Starting Offset For Due Date");
            string useStarting = cfg.GetValue<string>("Mazak", "Use Starting Offset");
            string decrPriority = cfg.GetValue<string>("Mazak", "Decrement Priority On Download");
            if (string.IsNullOrEmpty(useStarting)) {
                //useStarting is an old setting, so if it is missing use the new settings
                if (string.IsNullOrEmpty(useStartingForDue)) {
                    UseStartingOffsetForDueDate = false;
                } else {
                    UseStartingOffsetForDueDate = Convert.ToBoolean(useStartingForDue);
                }
                if (string.IsNullOrEmpty(decrPriority)) {
                    DecrementPriorityOnDownload = false;
                } else {
                    DecrementPriorityOnDownload = Convert.ToBoolean(decrPriority);
                }
			} else {
				UseStartingOffsetForDueDate = Convert.ToBoolean(useStarting);
                DecrementPriorityOnDownload = UseStartingOffsetForDueDate;
			}
            //Perhaps this should be a new setting, but if you don't check for pallets used once
            //then you don't care if all faces on a pallet are full so might as well use priority
            //which causes pallet positions to go empty.
            CheckPalletsUsedOnce = !UseStartingOffsetForDueDate && !DecrementPriorityOnDownload;


            // serial settings
            string serialPerMaterial = cfg.GetValue<string>("Mazak", "Assign Serial Per Material");
            if (!string.IsNullOrEmpty (serialPerMaterial)) {
				bool result;
				if (bool.TryParse(serialPerMaterial, out result)) {
                    if (!result)
                        serSettings.SerialType = SerialType.AssignOneSerialPerCycle;
				}
			}


#if DEBUG
            //MazakType = DatabaseAccess.MazakDbType.MazakSmooth;
            //dbPath = "172.16.11.6";
            MazakType = DatabaseAccess.MazakDbType.MazakVersionE;
            dbConnStr = @"\\172.16.11.14\mazak\NFMS\DB";
            UseStartingOffsetForDueDate = true;
#endif

            if (!System.IO.Directory.Exists(logPath))
				logTrace.TraceEvent(TraceEventType.Error, 0, "Configured Log CSV directory " + logPath + " does not exist.");

            logTrace.TraceData(TraceEventType.Warning, 0,
                "Configured UseStartingOffsetForDueDate = " + UseStartingOffsetForDueDate.ToString() + " and " +
                "DecrementPriorityOnDownload = " + DecrementPriorityOnDownload.ToString());

			jobLog = new BlackMaple.MachineFramework.JobLogDB();
			jobLog.Open(System.IO.Path.Combine(dataDirectory, "log.db"));

			insp = new BlackMaple.MachineFramework.InspectionDB(jobLog);
			insp.Open(System.IO.Path.Combine(dataDirectory, "insp.db"));
			jobDB = new BlackMaple.MachineFramework.JobDB();
            var jobInspName = System.IO.Path.Combine(dataDirectory, "jobinspection.db");
            if (System.IO.File.Exists(jobInspName))
                jobDB.Open(jobInspName);
            else
                jobDB.Open(System.IO.Path.Combine(dataDirectory, "mazakjobs.db"));

			database = new DatabaseAccess(dbConnStr, MazakType, false);
			IReadDataAccess readOnlyDb = new DatabaseAccess(dbConnStr, MazakType, true);
			if (MazakType == DatabaseAccess.MazakDbType.MazakWeb || MazakType == DatabaseAccess.MazakDbType.MazakSmooth)
				logDataLoader = new LogDataWeb(logPath);
			else {
				#if USE_OLEDB
				logDataLoader = new LogDataVerE(readOnlyDb);
				#else
				throw new Exception("Mazak Web and VerE are not supported on .NET core");
				#endif
			}
			hold = new HoldPattern(dataDirectory, database, holdTrace, true);
			loadOper = new LoadOperations(loadOperTrace, cfg);
			routing = new RoutingInfo(database, hold, jobDB, jobLog, insp, loadOper,
			                          CheckPalletsUsedOnce, UseStartingOffsetForDueDate, DecrementPriorityOnDownload,
			                          routingTrace);
			logTrans = new LogTranslation(jobLog, readOnlyDb, serSettings, logDataLoader, logTrace);

			logTrans.MachiningCompleted += HandleMachiningCompleted;
		}

		public void Halt()
		{
            routing.Halt();
			hold.Shutdown();
			logTrans.Halt();
			loadOper.Halt();
			jobDB.Close();
			insp.Close();
			jobLog.Close();
		}

		private TraceSource routingTrace = new TraceSource("Mazak", SourceLevels.All);
		private TraceSource holdTrace = new TraceSource("Hold Transitions", SourceLevels.All);
		private TraceSource loadOperTrace = new TraceSource("Load Operations", SourceLevels.All);
		private TraceSource logTrace = new TraceSource("Log", SourceLevels.All);

		public IEnumerable<System.Diagnostics.TraceSource> TraceSources()
		{
			return new TraceSource[] { routingTrace, holdTrace, loadOperTrace, logTrace };
		}

		public IInspectionControl InspectionControl()
		{
			return insp;
		}

		public IJobControl JobControl()
		{
			return routing;
		}

		public IOldJobDecrement OldJobDecrement()
		{
			return routing;
		}

		public IJobDatabase JobDatabase()
		{
			return jobDB;
		}

		public ILogDatabase LogDatabase()
		{
			return jobLog;
		}


		private void HandleMachiningCompleted(BlackMaple.MachineWatchInterface.LogEntry cycle, ReadOnlyDataSet dset)
		{
			foreach (LogMaterial mat in cycle.Material) {
				if (mat.MaterialID < 0 || mat.JobUniqueStr == null || mat.JobUniqueStr == "") {
					logTrace.TraceEvent(TraceEventType.Information, 0,
					                    "HandleMachiningCompleted: Skipping material id " + mat.MaterialID.ToString() +
					                    " part " + mat.PartName +
					                    " because the job unique string is empty");
					continue;
				}

				var job = RoutingInfo.RoutingForUnique(dset, mat.JobUniqueStr, MazakType);
				if (job == null) {
					logTrace.TraceEvent(TraceEventType.Information, 0, "Unable to make inspection decisions for material " + mat.MaterialID.ToString() +
					                 " completed at time " + cycle.EndTimeUTC.ToLocalTime().ToString() + " on pallet " + cycle.Pallet.ToString() +
					                 " part " + mat.PartName +
					                 " because the job " + mat.JobUniqueStr + " cannot be found");
					continue;
				}

				job.AddInspections(jobDB.LoadInspections(job.UniqueStr));
                foreach (var i in insp.LoadAllGlobalInspections())
                {
                    job.AddInspection(i.ConvertToJobInspection(job.PartName, job.NumProcesses));
                }

                bool hasInspections = false;
				foreach (var i in job.GetInspections()) {
					if (i.InspectSingleProcess <= 0 && mat.Process != mat.NumProcesses) {
						logTrace.TraceEvent(TraceEventType.Information, 0, "Skipping inspection for material " + mat.MaterialID.ToString() +
											" inspection type " + i.InspectionType +
											" completed at time " + cycle.EndTimeUTC.ToLocalTime().ToString() + " on pallet " + cycle.Pallet.ToString() +
											" part " + mat.PartName +
											" because the process is not the maximum process");

						continue;
					}
					if (i.InspectSingleProcess >= 1 && mat.Process != i.InspectSingleProcess) {
						logTrace.TraceEvent(TraceEventType.Information, 0, "Skipping inspection for material " + mat.MaterialID.ToString() +
											" inspection type " + i.InspectionType +
											" completed at time " + cycle.EndTimeUTC.ToLocalTime().ToString() + " on pallet " + cycle.Pallet.ToString() +
											" part " + mat.PartName +
											" process " + mat.Process.ToString() +
											" because the inspection is only on process " +
											i.InspectSingleProcess.ToString());

						continue;
					}

					hasInspections = true;
					var result = insp.MakeInspectionDecision(mat.MaterialID, job, i);
					logTrace.TraceEvent(TraceEventType.Information, 0,
					                 "Making inspection decision for " + i.InspectionType + " material " + mat.MaterialID.ToString() +
					                 " completed at time " + cycle.EndTimeUTC.ToLocalTime().ToString() + " on pallet " + cycle.Pallet.ToString() +
					                 " part " + mat.PartName +
					                 ".  The decision is " + result.ToString());
				}

				if (!hasInspections) {
					logTrace.TraceEvent(TraceEventType.Information, 0,
					                    "No inspection decisions made for material " + mat.MaterialID.ToString() +
					                    " completed at time " + cycle.EndTimeUTC.ToLocalTime().ToString() + " on pallet " + cycle.Pallet.ToString() +
					                    " part " + mat.PartName);
				}
			}
		}

        private DatabaseAccess.MazakDbType DetectMazakType(IConfig cfg, string localDbPath)
        {
            var webver = cfg.GetValue<bool>("Mazak", "Web Version");
            var smoothVer = cfg.GetValue<bool>("Mazak", "Smooth Version");

            if (webver)
                return DatabaseAccess.MazakDbType.MazakWeb;
            else if (smoothVer)
                return DatabaseAccess.MazakDbType.MazakSmooth;

            string testPath;
            if (string.IsNullOrEmpty(localDbPath))
            {
                testPath = "C:\\Mazak\\NFMS\\DB\\FCREADDAT01.mdb";
            }
            else
            {
                testPath = System.IO.Path.Combine(localDbPath, "FCREADDAT01.mdb");
            }

            if (System.IO.File.Exists(testPath))
            {
                //TODO: open database to check column existance for web vs E.
                logTrace.TraceEvent(TraceEventType.Warning, 0, "Assuming Mazak WEB version.  If this is incorrect it can be changed in the settings.");
                return DatabaseAccess.MazakDbType.MazakWeb;
            }
            else
            {
                logTrace.TraceEvent(TraceEventType.Warning, 0, "Assuming Mazak Smooth version.  If this is incorrect it can be changed in the settings.");
                return DatabaseAccess.MazakDbType.MazakSmooth;
            }
        }
	}

	public static class MazakProgram
	{
		public static void Main()
		{
			#if DEBUG
			var useService = false;
			#else
			var useService = true;
			#endif
			Program.Run(useService, new MazakBackend());
		}
	}
}