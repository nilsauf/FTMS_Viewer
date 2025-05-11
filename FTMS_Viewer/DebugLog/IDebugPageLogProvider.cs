namespace FTMS_Viewer.DebugLog;

using System;

public interface IDebugPageLogProvider
{
	IObservable<DebugLogItem> ObserveLogItems();
}
