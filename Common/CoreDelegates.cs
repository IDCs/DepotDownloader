using Microsoft.CSharp.RuntimeBinder;

namespace Common
{
  public struct Defaults
  {
    public static int TIMEOUT_MS = 5000;
    public static int USER_INPUT_TIMEOUT_MS = 60000;
  }

  #region Context

  public class ContextDelegates
  {
    private Func<object, Task<object>> mGetSteamId;
    private Func<object, Task<object>> mGetExistingDataFile;
    private Func<object, Task<object>> mGetGameExecutable;
    private Func<object, Task<object>> mGetDepotIds;
    private Func<object[], Task<object>> mGetExistingDataFileList;
    private Func<object[], Task<object>> mGetGameFileList;

    public ContextDelegates(dynamic source)
    {
      mGetSteamId = source.getSteamId;
      mGetExistingDataFile = source.getExistingDataFile;
      mGetExistingDataFileList = source.getExistingDataFileList;
      mGetGameFileList = source.getGameFileList;
      mGetGameExecutable = source.getGameExecutable;
      mGetDepotIds = source.getDepotIds;
    }

    public async Task<byte[]> GetExistingDataFile(string dataFile)
    {
      object res = await Util.Timeout(mGetExistingDataFile(dataFile), Defaults.TIMEOUT_MS);
      return (byte[])res;
    }

    public async Task<string[]> GetExistingDataFileList()
    {
      object res = await Util.Timeout(mGetExistingDataFileList(new object[] { }), Defaults.TIMEOUT_MS);
      return ((object[])res).Select(iter => (string)iter).ToArray();
    }

    public async Task<string[]> GetExistingDataFileList(string folderPath, string searchFilter, bool isRecursive)
    {
      object[] Params = new object[] { folderPath, searchFilter, isRecursive };
      object res = await Util.Timeout(mGetExistingDataFileList(Params), Defaults.TIMEOUT_MS);
      return ((object[])res).Select(iter => (string)iter).ToArray();
    }

    public async Task<string[]> GetGameFileList()
    {
      object res = await Util.Timeout(mGetGameFileList(new object[] { }), Defaults.TIMEOUT_MS);
      return ((object[])res).Select(iter => (string)iter).ToArray();
    }

    public async Task<string> GetGameExecutable()
    {
      object res = await Util.Timeout(mGetGameExecutable(new object()), Defaults.TIMEOUT_MS);
      return (string)res;
    }

    public async Task<List<uint>> GetDepotIds()
    {
      object res = await Util.Timeout(mGetDepotIds(new object()), Defaults.TIMEOUT_MS);
      return ((object[])res).Select(iter => Convert.ToUInt32(iter)).ToList();
    }
  }

  #endregion

  #region UI

  namespace ui
  {
    public class Delegates
    {
      private Func<object, Task<object>> mReportError;
      private Func<object, Task<object>> mRequestSteamGuard;
      private Func<object, Task<object>> mRequest2FA;
      private Func<object, Task<object>> mRequestCredentials;
      private Func<object, Task<object>> mIsVerifyingFiles;
      private Func<object, Task<object>> mTimedOut;
      private Func<object, Task<object>> mRateLimitExceeded;
      private Func<object[], Task<object>> mReportMismatch;

      public Delegates(dynamic source)
      {
        mReportError = source.reportError;
        mRequestCredentials = source.requestCredentials;
        mRequestSteamGuard = source.requestSteamGuard;
        mRequest2FA = source.request2FA;
        mReportMismatch = source.reportMismatch;
        mIsVerifyingFiles = source.isVerifyingFiles;
        mTimedOut = source.timedOut;
        mRateLimitExceeded = source.ratelimitExceeded;
      }
      
      public async Task<bool> IsVerifyingFiles()
      {
        await mIsVerifyingFiles(null);
        return true;
      }

      public async Task<bool> RateLimitExceeded()
      {
        await mRateLimitExceeded(null);
        return true;
      }

      public async Task<bool> TimedOut(OpType op)
      {
        await mTimedOut((int)op);
        return true;
      }

      public async Task<string[]> RequestCredentials(bool retry = false)
      {
        object res = await mRequestCredentials(retry);
        return ((object[])res).Select(iter => (string)iter).ToArray();
      }

      public async Task<string> RequestSteamGuard()
      {
        object res = await Util.Timeout(mRequestSteamGuard(null), Defaults.USER_INPUT_TIMEOUT_MS);
        return (string)res;
      }

      public async Task<string> Request2FA()
      {
        object res = await Util.Timeout(mRequest2FA(null), Defaults.USER_INPUT_TIMEOUT_MS);
        return (string)res;
      }

      public async void ReportError(string title, string message, string details)
      {
        try
        {
          await Util.Timeout(mReportError(new Dictionary<string, dynamic> {
                        { "title", title },
                        { "message", message },
                        { "details", details }
                    }), Defaults.TIMEOUT_MS);
        }
        catch (Exception e)
        {
          Console.WriteLine("exception in report error: {0}", e);
        }
      }

      public async Task<string[]> ReportMismatch(string[] invalidFiles)
      {
        object[] Params = new object[] { invalidFiles };
        object res = await Util.Timeout(mReportMismatch(Params), Defaults.USER_INPUT_TIMEOUT_MS);
        return ((object[])res).Select(iter => (string)iter).ToArray();
      }
    }
  }

  #endregion

  public class CoreDelegates
  {
    private ContextDelegates mContextDelegates;
    private ui.Delegates mUIDelegates;

    public CoreDelegates(dynamic source)
    {
      mContextDelegates = new ContextDelegates(source.context);
      mUIDelegates = new ui.Delegates(source.ui);
    }

    public ContextDelegates context
    {
      get
      {
        return mContextDelegates;
      }
    }

    public ui.Delegates ui
    {
      get
      {
        return mUIDelegates;
      }
    }
  }
}
