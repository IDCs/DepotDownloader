using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using SteamKit2;

using Common;

namespace DepotDownloader
{
  public class InvalidCredentialsException : Exception
  {
    public InvalidCredentialsException() : base() { }
  }

  public static class Exec
  {
    private static CoreDelegates _instance;
    public static CoreDelegates Instance { get { return _instance; } }

    public static IParameters Parameters = null;

    //public static int _steamInitRetries = 0;
    public static async Task<Dictionary<string, object>> VerifyFileIntegrity(IParameters parameters,
                                                                             CoreDelegates core)
    {
      Parameters = parameters;
      if (core != null && _instance == null)
      {
        _instance = core;
      }

      AccountSettingsStore.LoadFromFile("account.config");

      #region Common Options

      var username = parameters.Username;
      var password = parameters.Password;
      ContentDownloader.Config.RememberPassword = parameters.RememberPassword;

      ContentDownloader.Config.DownloadManifestOnly = parameters.ManifestOnly;

      var cellId = parameters?.CellId ?? -1;
      if (cellId == -1)
      {
        cellId = 0;
      }

      ContentDownloader.Config.CellID = cellId;

      var fileList = parameters?.FileList ?? null;

      if (fileList != null)
      {
        try
        {
          var files = fileList.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

          ContentDownloader.Config.UsingFileList = true;
          ContentDownloader.Config.FilesToDownload= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
          ContentDownloader.Config.FilesToDownloadRegex = new List<Regex>();

          foreach (var fileEntry in files)
          {
            if (fileEntry.StartsWith("regex:"))
            {
              var rgx = new Regex(fileEntry.Substring(6), RegexOptions.Compiled | RegexOptions.IgnoreCase);
              ContentDownloader.Config.FilesToDownloadRegex.Add(rgx);
            }
            else
            {
              ContentDownloader.Config.FilesToDownload.Add(fileEntry.Replace('\\', '/'));
            }
          }

          Console.WriteLine("Using filelist: '{0}'.", fileList);
        }
        catch (Exception ex)
        {
          return new Dictionary<string, object>
          {
              { "message", "Warning: Unable to load filelist" },
              { "stack", ex }
          };
        }
      }

      ContentDownloader.Config.InstallDirectory = parameters?.InstallDirectory ?? null;

      ContentDownloader.Config.VerifyAll = parameters?.VerifyAll ?? false;
      ContentDownloader.Config.MaxServers = parameters?.MaxServers ?? 20;
      ContentDownloader.Config.MaxDownloads = parameters?.MaxDownloads ?? 8;
      ContentDownloader.Config.MaxServers = Math.Max(ContentDownloader.Config.MaxServers, ContentDownloader.Config.MaxDownloads);
      ContentDownloader.Config.LoginID = parameters?.LoginId ?? null;

      #endregion

      var appId = (parameters?.AppId != null) ? uint.Parse(parameters?.AppId) : ContentDownloader.INVALID_APP_ID;
      if (appId == ContentDownloader.INVALID_APP_ID)
      {
        return new Dictionary<string, object>
        {
            { "message", "Error: SteamAppId not specified" },
        };
      }

      var pubFile = parameters?.PubFile ?? ContentDownloader.INVALID_MANIFEST_ID;
      var ugcId = parameters?.UgcId ?? ContentDownloader.INVALID_MANIFEST_ID;
      if (pubFile != ContentDownloader.INVALID_MANIFEST_ID)
      {
        string[] creds = new string[] { username, password };
        if (ContentDownloader._logonDetails?.Password == null)
        {
          creds = await core.ui.RequestCredentials();
          ContentDownloader._logonDetails.Username = creds[0];
          ContentDownloader._logonDetails.Password = creds[1];
        }
        #region Pubfile Downloading

        if (await InitializeSteam(creds[0], creds[1], core))
        {
          try
          {
            await ContentDownloader.DownloadPubfileAsync(appId, pubFile).ConfigureAwait(false);
          }
          catch (Exception ex) when (
              ex is ContentDownloaderException
              || ex is OperationCanceledException)
          {
            core.ui.ReportError("Error", "Failed to download content", ex.Message);
            Console.WriteLine(ex.Message);
            return new Dictionary<string, object>
            {
                { "message", ex.Message },
            };
          }
          catch (Exception e)
          {
            core.ui.ReportError("Error", "Failed to download content", e.Message);
            return new Dictionary<string, object>
            {
                { "message", e.Message },
            };
          }
          finally
          {
            ContentDownloader.ShutdownSteam3();
          }
        }
        else
        {
          ContentDownloader._logonDetails = null;
          username = null;
          password = null;
          throw new InvalidCredentialsException();
        }

        #endregion
      }
      else if (ugcId != ContentDownloader.INVALID_MANIFEST_ID)
      {
        string[] creds = new string[] { username, password };
        if (ContentDownloader._logonDetails?.Password == null)
        {
          creds = await core.ui.RequestCredentials();
          ContentDownloader._logonDetails.Username = creds[0];
          ContentDownloader._logonDetails.Password = creds[1];
        }
        #region UGC Downloading
        if (await InitializeSteam(creds[0], creds[1], core))
        {
          try
          {
            await ContentDownloader.DownloadUGCAsync(appId, ugcId).ConfigureAwait(false);
          }
          catch (Exception ex) when (
              ex is ContentDownloaderException
              || ex is OperationCanceledException)
          {
            return new Dictionary<string, object>
            {
                { "error", ex.Message },
            };
          }
          catch (Exception e)
          {
            return new Dictionary<string, object>
            {
                { "message", e.Message },
            };
            throw;
          }
          finally
          {
            ContentDownloader.ShutdownSteam3();
          }
        }
        else
        {
          return new Dictionary<string, object>
          {
              { "message", "Error: InitializeSteam failed" },
          };
        }

        #endregion
      }
      else
      {
        #region App downloading

        var branch = parameters?.Branch ?? ContentDownloader.DEFAULT_BRANCH;
        ContentDownloader.Config.BetaPassword = parameters?.BetaBranchPassword ?? null;

        var depotManifestIds = new List<(uint, ulong)>();
        var isUGC = false;
        List<uint> installeddepotIds = new List<uint>();
        if (parameters?.DepotIdList == null)
        {
          try
          {
            installeddepotIds = await core.context.GetDepotIds();
          }
          catch (Exception ex)
          {
            // nop
          }
        }
        var depotIdList = parameters?.DepotIdList ?? installeddepotIds;
        var manifestIdList = parameters?.ManifestIdList ?? new List<ulong>();
        if (manifestIdList.Count > 0)
        {
          if (depotIdList.Count != manifestIdList.Count)
          {
            return new Dictionary<string, object>
            {
                { "message", "Error: -manifest requires one id for every -depot specified" },
            };
          }

          var zippedDepotManifest = depotIdList.Zip(manifestIdList, (depotId, manifestId) => (depotId, manifestId));
          depotManifestIds.AddRange(zippedDepotManifest);
        }
        else
        {
          depotManifestIds.AddRange(depotIdList.Select(depotId => (depotId, ContentDownloader.INVALID_MANIFEST_ID)));
        }

        try
        {
          var result = await DownloadAppAsync(username, password, appId, depotManifestIds, branch, isUGC, core);
        } catch (Exception ex)
        {
        }
        finally
        {
          //ContentDownloader.ShutdownSteam3();
        }

        #endregion
      }

      return new Dictionary<string, object>();
    }

    static async Task<Dictionary<string, object>> DownloadAppAsync(string username, string password, uint appId, List<(uint, ulong)> depotManifestIds, string branch, bool isUGC, CoreDelegates core)
    {
      if (await InitializeSteam(username, password, core))
      {
        try
        {
          await ContentDownloader.DownloadAppAsync(core, appId, depotManifestIds, branch, null, null, null, false, isUGC).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is ContentDownloaderException
            || ex is OperationCanceledException)
        {

          core.ui.ReportError("Error", "Failed to download content", ex.Message);
          Console.WriteLine(ex.Message);
          return new Dictionary<string, object>
            {
                { "message", ex.Message },
            };
        }
        catch (Exception ex) when (
            ex is ContentDownloaderAccountException)
        {
          bool retry = ContentDownloader._logonDetails?.Password != null;
          if (retry && ContentDownloader.CredentialsStatus.IsValid)
          {
            core.ui.ReportError("Failed to verify files", "Your Steam account does not own a license for this game", ex.ToString());
            throw ex;
          }
          string[] creds = await core.ui.RequestCredentials(retry);
          if (ContentDownloader._logonDetails != null)
          {
            ContentDownloader._logonDetails.Username = creds[0];
            ContentDownloader._logonDetails.Password = creds[1];
          }
          Console.WriteLine("Received credentials");
          try
          {
            await DownloadAppAsync(creds[0], creds[1], appId, depotManifestIds, branch, isUGC, core);
          } catch (Exception)
          {
            throw;
          }
        }
        catch (Exception e)
        {
          Console.WriteLine("Download failed to due to an unhandled exception: {0}", e.Message);
          throw;
        }
      }
      else
      {
        //if (_steamInitRetries > 2)
        //{
        //  Console.WriteLine("Error: InitializeSteam failed");
        //  return new Dictionary<string, object>
        //  {
        //    { "error", "Error: InitializeSteam failed" },
        //  };
        //}
        //_steamInitRetries++;
        //await Task.Delay(Defaults.USER_INPUT_TIMEOUT_MS);
        //try {
        //  await DownloadAppAsync(username, password, appId, depotManifestIds, branch, isUGC, core);
        //} catch (Exception)
        //{
        //}
      }

      return new Dictionary<string, object>
      {
          { "message", "download finished" },
      };
    }

    static async Task<bool> InitializeSteam(string username, string password, CoreDelegates core)
    {
      if (ContentDownloader._logonDetails != null)
      {
        ContentDownloader.Config.SuppliedPassword = password;
        return await ContentDownloader.InitializeSteam3(ContentDownloader._logonDetails.Username, ContentDownloader._logonDetails.Password, core);
      }
      if (username != null && password == null && (!ContentDownloader.Config.RememberPassword || !AccountSettingsStore.Instance.LoginKeys.ContainsKey(ContentDownloader._logonDetails.Username)))
      {
        string[] creds = await core.ui.RequestCredentials();
        ContentDownloader._logonDetails.Username = creds[0];
        ContentDownloader._logonDetails.Password = creds[1];
      }
      else if (username == null)
      {
        Console.WriteLine("No username given. Using anonymous account with dedicated server subscription.");
      }

      // capture the supplied password in case we need to re-use it after checking the login key
      ContentDownloader.Config.SuppliedPassword = password;

      return await ContentDownloader.InitializeSteam3(username, password, core);
    }
  }
}
