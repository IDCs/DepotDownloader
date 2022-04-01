using Microsoft.CSharp.RuntimeBinder;

namespace Common
{
  using SelectCB = Action<int, int, int[]>;
  using ContinueCB = Action<bool, int>;
  using CancelCB = Action;

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
    private Func<object[], Task<object>> mGetExistingDataFileList;
    private Func<object[], Task<object>> mGetGameFileList;

    public ContextDelegates(dynamic source)
    {
      mGetSteamId = source.getSteamId;
      mGetExistingDataFile = source.getExistingDataFile;
      mGetExistingDataFileList = source.getExistingDataFileList;
      mGetGameFileList = source.getGameFileList;
      mGetGameExecutable = source.getGameExecutable;
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
  }

  #endregion

  #region UI

  public struct HeaderImage
  {
    public string path;
    public bool showFade;
    public int height;

    public HeaderImage(string path, bool showFade, int height) : this()
    {
      this.path = path;
      this.showFade = showFade;
      this.height = height;
    }
  }

  namespace ui
  {
    public struct Option
    {
      public int id;
      public bool selected;
      public bool preset;
      public string name;
      public string description;
      public string image;
      public string type;
      public string conditionMsg;

      public Option(int id, string name, string description, string image, bool selected, bool preset, string type, string conditionMsg) : this()
      {
        this.id = id;
        this.name = name;
        this.description = description;
        this.image = image;
        this.selected = selected;
        this.preset = preset;
        this.type = type;
        this.conditionMsg = conditionMsg;
      }
    }

    public struct Group
    {
      public int id;
      public string name;
      public string type;
      public Option[] options;

      public Group(int id, string name, string type, Option[] options) : this()
      {
        this.id = id;
        this.name = name;
        this.type = type;
        this.options = options;
      }
    }

    public struct GroupList
    {
      public Group[] group;
      public string order;
    }

    public struct InstallerStep
    {
      public int id;
      public string name;
      public bool visible;
      public GroupList optionalFileGroups;

      public InstallerStep(int id, string name, bool visible) : this()
      {
        this.id = id;
        this.name = name;
        this.visible = visible;
      }
    }

    struct StartParameters
    {
      public string moduleName;
      public HeaderImage image;
      public Func<object, Task<object>> select;
      public Func<object, Task<object>> cont;
      public Func<object, Task<object>> cancel;

      public StartParameters(string moduleName, HeaderImage image, SelectCB select, ContinueCB cont, CancelCB cancel)
      {
        this.moduleName = moduleName;
        this.image = image;
        this.select = async (dynamic selectPar) =>
        {
          object[] pluginObjs = selectPar.plugins;
          IEnumerable<int> pluginIds = pluginObjs.Select(id => (int)id);
          select(selectPar.stepId, selectPar.groupId, pluginIds.ToArray());
          return await Task.FromResult<object>(null);
        };

        this.cont = async (dynamic continuePar) =>
        {
          string direction = continuePar.direction;
          int currentStep = -1;
          try
          {
            currentStep = continuePar.currentStepId;
          }
          catch (RuntimeBinderException)
          {
            // no problem, we'll just not validate if the message is for the expected page
          }
          cont(direction == "forward", currentStep);
          return await Task.FromResult<object>(null);
        };

        this.cancel = async (dynamic dummy) =>
        {
          cancel();
          return await Task.FromResult<object>(null);
        };
      }
    }

    struct UpdateParameters
    {
      public InstallerStep[] installSteps;
      public int currentStep;

      public UpdateParameters(InstallerStep[] installSteps, int currentStep)
      {
        this.installSteps = installSteps;
        this.currentStep = currentStep;
      }
    }

    public class Delegates
    {
      private Func<object, Task<object>> mStartDialog;
      private Func<object, Task<object>> mEndDialog;
      private Func<object, Task<object>> mUpdateState;
      private Func<object, Task<object>> mReportError;
      private Func<object, Task<object>> mRequestSteamGuard;
      private Func<object, Task<object>> mRequest2FA;
      private Func<object, Task<object>> mRequestCredentials;
      private Func<object, Task<object>> mReportMismatch;

      public Delegates(dynamic source)
      {
        mStartDialog = source.startDialog;
        mEndDialog = source.endDialog;
        mUpdateState = source.updateState;
        mReportError = source.reportError;
        mRequestCredentials = source.requestCredentials;
        mRequestSteamGuard = source.requestSteamGuard;
        mRequest2FA = source.request2FA;
        mReportMismatch = source.reportMismatch;
      }

      public async void StartDialog(string moduleName, HeaderImage image, SelectCB select, ContinueCB cont, CancelCB cancel)
      {
        try
        {
          await Util.Timeout(
             mStartDialog(new StartParameters(moduleName, image, select, cont, cancel)), Defaults.TIMEOUT_MS);
        }
        catch (Exception e)
        {
          Console.WriteLine("exception in start dialog: {0}", e);
        }
      }

      public async void EndDialog()
      {
        try
        {
          await Util.Timeout(mEndDialog(null), Defaults.TIMEOUT_MS);
        }
        catch (Exception e)
        {
          Console.WriteLine("exception in end dialog: {0}", e);
        }
      }

      public async void UpdateState(InstallerStep[] installSteps, int currentStep)
      {
        try
        {
          await Util.Timeout(mUpdateState(new UpdateParameters(installSteps, currentStep)), Defaults.TIMEOUT_MS);
        }
        catch (Exception e)
        {
          Console.WriteLine("exception in update state: {0}", e);
        }
      }

      public async Task<string[]> RequestCredentials()
      {
        object res = await Util.Timeout(mRequestCredentials(null), Defaults.USER_INPUT_TIMEOUT_MS);
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

      public async Task<bool> ReportMismatch(string[] invalidFiles)
      {
        object res = await Util.Timeout(mReportMismatch(invalidFiles), Defaults.USER_INPUT_TIMEOUT_MS);
        return (bool)res;
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
