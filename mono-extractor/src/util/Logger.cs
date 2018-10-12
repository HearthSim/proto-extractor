using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace protoextractor.util
{
	class Logger
	{
		// 2017-01-10 13:50:17Z [INFO]  Message
		private const string LOGFORMAT = "{0:u} [{1}]\t {2}";
		// 2017-01-10 13:50:17Z [INFO]  Message\nStacktrace
		private const string EXCEPT_FORMAT = "{0:u} [{1}]\t {2}\n{3}";

		// Syntax of log blocks
		private const string BLOCKFORMAT = "------------ BLOCK {0}: {1} ------------";
		private const string BLOCK_OPEN = "OPEN";
		private const string BLOCK_CLOSE = "CLOSE";

		private const string ERROR = "ERROR";
		private const string EXCEPTION = "EXCEPTION";
		private const string INFO = "INFO";
		private const string WARN = "WARN";
		private const string DEBUG = "DEBUG";

		// TRUE if debug messages should be written
		private bool DebugMode;

		// The stream to write messages to.
		private TextWriter OutStream;

		// Keeps track of opened blocks in logfile.
		private Stack<string> LogBlocks;

		// Initialises a default Logger instance that writes to console.
		// No debug messages!
		public Logger()
		{
			DebugMode = false;
			OutStream = Console.Out;
			LogBlocks = new Stack<string>();
		}

		// Initialise the logger according to the options provided
		public Logger(Options options) : this()
		{
			SetParams(options);
		}

		~Logger()
		{
			try
			{
				OutStream.Flush();
				OutStream.Dispose();
			}
			catch (Exception)
			{
				// Do nothing
			}
		}

		public void SetParams(Options options)
		{
			DebugMode = options.DebugMode;
			SetupLogStream(options.LogFile);
		}

		private void SetupLogStream(string logfile)
		{
			// No log file given; assign standard out
			if (logfile == null || logfile.Length == 0)
			{
				OutStream = Console.Out;
				return;
			}

			// Do we even check if the file exists?
			try
			{
				logfile = Path.GetFullPath(logfile);
				FileStream fStream = File.OpenWrite(logfile);
				OutStream = new StreamWriter(fStream);
			}
			catch (Exception e)
			{
				OutStream = Console.Error;
				Exception("Could not output a logfile to the given path!", e);
			}
		}

		public void Debug(string message, params object[] fills)
		{
			if (DebugMode == true)
			{
				message = string.Format(message, fills);
				var msg = string.Format(LOGFORMAT, DateTime.UtcNow, DEBUG, message);
				OutStream.WriteLine(msg);
			}
		}

		public void Info(string message, params object[] fills)
		{
			message = string.Format(message, fills);
			var msg = string.Format(LOGFORMAT, DateTime.UtcNow, INFO, message);
			OutStream.WriteLine(msg);
		}

		public void Warn(string message, params object[] fills)
		{
			message = string.Format(message, fills);
			var msg = string.Format(LOGFORMAT, DateTime.UtcNow, WARN, message);
			OutStream.WriteLine(msg);
		}

		public void Exception(string message, Exception e = null, params object[] fills)
		{
			message = string.Format(message, fills);
			// Show stacktrace only if debug mode was turned on
			var stacktraceText = string.Format("--->{0}<---\n{1}", e?.Message, e?.StackTrace);
			var stacktrace = (DebugMode == true) ? stacktraceText : "";

			var msg = string.Format(EXCEPT_FORMAT, DateTime.UtcNow, EXCEPTION, message, stacktrace);
			OutStream.WriteLine(msg);
		}

		public void OpenBlock(string blockName)
		{
			var message = string.Format(BLOCKFORMAT, BLOCK_OPEN, blockName);
			var msg = string.Format(LOGFORMAT, DateTime.UtcNow, "", message);
			OutStream.WriteLine(msg);

			// Store the blockName on the stack for easy retrieveal when block closes.
			LogBlocks.Push(blockName);
		}

		public void CloseBlock()
		{
			// Retrieve block name from stack.
			var blockName = LogBlocks.Pop();

			var message = string.Format(BLOCKFORMAT, BLOCK_CLOSE, blockName);
			var msg = string.Format(LOGFORMAT, DateTime.UtcNow, "", message);
			OutStream.WriteLine(msg);
			// Write an additionall empty line
			OutStream.WriteLine();
		}
	}
}
