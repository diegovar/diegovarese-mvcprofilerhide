﻿using System;
using System.Collections.Generic;
using System.Data.Common;


namespace MvcMiniProfiler
{
    /// <summary>
    /// Categories of sql statements.
    /// </summary>
    public enum ExecuteType : byte
    {
        /// <summary>
        /// Unknown
        /// </summary>
        None = 0,

        /// <summary>
        /// DML statements that alter database state, e.g. INSERT, UPDATE
        /// </summary>
        NonQuery = 1,

        /// <summary>
        /// Statements that return a single record
        /// </summary>
        Scalar = 2,

        /// <summary>
        /// Statements that iterate over a result set
        /// </summary>
        Reader = 3
    }

    // TODO: refactor this out into MiniProfiler
    /// <summary>
    /// Contains helper code to time sql statements.
    /// </summary>
    public class SqlProfiler
    {
        Dictionary<Tuple<object, ExecuteType>, SqlTiming> _inProgress = new Dictionary<Tuple<object, ExecuteType>, SqlTiming>();
        Dictionary<DbDataReader, SqlTiming> _inProgressReaders = new Dictionary<DbDataReader, SqlTiming>();

        /// <summary>
        /// The profiling session this SqlProfiler is part of.
        /// </summary>
        public MiniProfiler Profiler { get; private set; }

        /// <summary>
        /// Returns a new SqlProfiler to be used in the 'profiler' session.
        /// </summary>
        public SqlProfiler(MiniProfiler profiler)
        {
            Profiler = profiler;
        }

        /// <summary>
        /// Tracks when 'command' is started.
        /// </summary>
        public void ExecuteStartImpl(DbCommand command, ExecuteType type)
        {
            var id = Tuple.Create((object)command, type);
            var sqlTiming = new SqlTiming(command, type, Profiler);

            _inProgress[id] = sqlTiming;
        }

        /// <summary>
        /// Finishes profiling for 'command', recording durations.
        /// </summary>
        public void ExecuteFinishImpl(DbCommand command, ExecuteType type, DbDataReader reader = null)
        {
            var id = Tuple.Create((object)command, type);
            var current = _inProgress[id];
            current.ExecutionComplete(isReader: reader != null);
            _inProgress.Remove(id);
            if (reader != null)
            {
                _inProgressReaders[reader] = current;
            }
        }

        /// <summary>
        /// Called when 'reader' finishes its iterations and is closed.
        /// </summary>
        public void ReaderFinishedImpl(DbDataReader reader)
        {
            SqlTiming stat;
            // this reader may have been disposed/closed by reader code, not by our using()
            if (_inProgressReaders.TryGetValue(reader, out stat))
            {
                stat.ReaderFetchComplete();
                _inProgressReaders.Remove(reader);
            }
        }
    }

    /// <summary>
    /// Helper methods that allow operation on SqlProfilers, regardless of their instantiation.
    /// </summary>
    public static class SqlProfilerExtensions
    {
        /// <summary>
        /// Tracks when 'command' is started.
        /// </summary>
        public static void ExecuteStart(this SqlProfiler sqlProfiler, DbCommand command, ExecuteType type)
        {
            if (sqlProfiler == null) return;
            sqlProfiler.ExecuteStartImpl(command, type);
        }

        /// <summary>
        /// Finishes profiling for 'command', recording durations.
        /// </summary>
        public static void ExecuteFinish(this SqlProfiler sqlProfiler, DbCommand command, ExecuteType type, DbDataReader reader = null)
        {
            if (sqlProfiler == null) return;
            sqlProfiler.ExecuteFinishImpl(command, type, reader);
        }

        /// <summary>
        /// Called when 'reader' finishes its iterations and is closed.
        /// </summary>
        public static void ReaderFinish(this SqlProfiler sqlProfiler, DbDataReader reader)
        {
            if (sqlProfiler == null) return;
            sqlProfiler.ReaderFinishedImpl(reader);
        }

    }
}