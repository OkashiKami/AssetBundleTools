using System;
using UnityEngine;
using uTinyRipper;
using ILogger = uTinyRipper.ILogger;
using LogType = uTinyRipper.LogType;

namespace uTinyRipperGUI
{
    public sealed class OutputLogger : ILogger
	{
		public void Log(LogType type, LogCategory category, string message)
		{
			switch(type)
            {
				case LogType.Info: Debug.Log($"{category}: {message}"); break;
				case LogType.Warning: Debug.LogWarning($"{category}: {message}"); break;
				case LogType.Error: Debug.LogError($"{category}: {message}"); break;

			}
		}

		private void LogInner(LogType type, LogCategory category, string message)
		{
			switch (type)
			{
				case LogType.Info: Debug.Log($"{category}: {message}"); break;
				case LogType.Warning: Debug.LogWarning($"{category}: {message}"); break;
				case LogType.Error: Debug.LogError($"{category}: {message}"); break;

			}
		}
	}
}
