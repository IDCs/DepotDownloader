using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SteamKit2;
using SteamKit2.Internal;

using Common;

namespace DepotDownloader
{
  class Steam3Session
  {
    public class Credentials
    {
      public bool LoggedOn { get; set; }
      public bool WaitingForAdditionalAuth { get; set; }
      public ulong SessionToken { get; set; }

      public bool IsValid
      {
        get { return LoggedOn; }
      }
    }

    public ReadOnlyCollection<SteamApps.LicenseListCallback.License> Licenses
    {
      get;
      private set;
    }

    public Dictionary<uint, ulong> AppTokens { get; private set; }
    public Dictionary<uint, ulong> PackageTokens { get; private set; }
    public Dictionary<uint, byte[]> DepotKeys { get; private set; }
    public ConcurrentDictionary<string, TaskCompletionSource<SteamApps.CDNAuthTokenCallback>> CDNAuthTokens { get; private set; }
    public Dictionary<uint, SteamApps.PICSProductInfoCallback.PICSProductInfo> AppInfo { get; private set; }
    public Dictionary<uint, SteamApps.PICSProductInfoCallback.PICSProductInfo> PackageInfo { get; private set; }
    public Dictionary<string, byte[]> AppBetaPasswords { get; private set; }

    public SteamClient steamClient;
    public SteamUser steamUser;
    public SteamContent steamContent;
    readonly SteamApps steamApps;
    readonly SteamCloud steamCloud;
    readonly SteamUnifiedMessages.UnifiedService<IPublishedFile> steamPublishedFile;
    readonly List<EResult> eInvalidAuthCode = new List<EResult>() { EResult.ExpiredLoginAuthCode, EResult.InvalidLoginAuthCode };

    readonly CallbackManager callbacks;

    readonly bool authenticatedUser;
    bool bConnected;
    bool bConnecting;
    bool bAborted;
    bool bDidDisconnect;
    bool bDidReceiveLoginKey;
    int connectionBackoff;
    int seq; // more hack fixes
    DateTime connectTime;

    // output
    readonly Credentials credentials;

    static readonly TimeSpan STEAM3_TIMEOUT = TimeSpan.FromSeconds(30);


    public Steam3Session(CoreDelegates core)
    {
      this.authenticatedUser = ContentDownloader._logonDetails.Username != null;
      this.credentials = new Credentials();
      this.bConnected = false;
      this.bConnecting = false;
      this.bAborted = false;
      this.bDidDisconnect = false;
      this.bDidReceiveLoginKey = false;
      this.seq = 0;

      this.AppTokens = new Dictionary<uint, ulong>();
      this.PackageTokens = new Dictionary<uint, ulong>();
      this.DepotKeys = new Dictionary<uint, byte[]>();
      this.CDNAuthTokens = new ConcurrentDictionary<string, TaskCompletionSource<SteamApps.CDNAuthTokenCallback>>();
      this.AppInfo = new Dictionary<uint, SteamApps.PICSProductInfoCallback.PICSProductInfo>();
      this.PackageInfo = new Dictionary<uint, SteamApps.PICSProductInfoCallback.PICSProductInfo>();
      this.AppBetaPasswords = new Dictionary<string, byte[]>();

      var clientConfiguration = SteamConfiguration.Create(config =>
          config
              .WithHttpClientFactory(HttpClientFactory.CreateHttpClient)
      );

      this.steamClient = new SteamClient(clientConfiguration);

      this.steamUser = this.steamClient.GetHandler<SteamUser>();
      this.steamApps = this.steamClient.GetHandler<SteamApps>();
      this.steamCloud = this.steamClient.GetHandler<SteamCloud>();
      var steamUnifiedMessages = this.steamClient.GetHandler<SteamUnifiedMessages>();
      this.steamPublishedFile = steamUnifiedMessages.CreateService<IPublishedFile>();
      this.steamContent = this.steamClient.GetHandler<SteamContent>();

      this.callbacks = new CallbackManager(this.steamClient);

      this.callbacks.Subscribe<SteamClient.ConnectedCallback>(ConnectedCallback);
      this.callbacks.Subscribe<SteamClient.DisconnectedCallback>(DisconnectedCallback);
      this.callbacks.Subscribe<SteamUser.LoggedOnCallback>((logOncallback) => LogOnCallback(logOncallback, core));
      this.callbacks.Subscribe<SteamUser.SessionTokenCallback>(SessionTokenCallback);
      this.callbacks.Subscribe<SteamApps.LicenseListCallback>(LicenseListCallback);
      this.callbacks.Subscribe<SteamUser.UpdateMachineAuthCallback>(UpdateMachineAuthCallback);
      this.callbacks.Subscribe<SteamUser.LoginKeyCallback>(LoginKeyCallback);

      Console.Write("Connecting to Steam3...");

      if (authenticatedUser)
      {
        var fi = new FileInfo(String.Format("{0}.sentryFile", ContentDownloader._logonDetails.Username));
        if (AccountSettingsStore.Instance.SentryData != null && AccountSettingsStore.Instance.SentryData.ContainsKey(ContentDownloader._logonDetails.Username))
        {
          ContentDownloader._logonDetails.SentryFileHash = Util.SHAHash(AccountSettingsStore.Instance.SentryData[ContentDownloader._logonDetails.Username]);
        }
        else if (fi.Exists && fi.Length > 0)
        {
          var sentryData = File.ReadAllBytes(fi.FullName);
          ContentDownloader._logonDetails.SentryFileHash = Util.SHAHash(sentryData);
          AccountSettingsStore.Instance.SentryData[ContentDownloader._logonDetails.Username] = sentryData;
          AccountSettingsStore.Save();
        }
      }

      Connect();
    }

    public delegate bool WaitCondition();

    private readonly object steamLock = new object();

    public bool WaitUntilCallback(Action submitter, WaitCondition waiter)
    {
      while (!bAborted && !waiter())
      {
        lock (steamLock)
        {
          submitter();
        }

        var seq = this.seq;
        do
        {
          lock (steamLock)
          {
            WaitForCallbacks();
          }
        } while (!bAborted && this.seq == seq && !waiter());
      }

      return bAborted;
    }

    public Credentials WaitForCredentials()
    {
      if (credentials.IsValid || bAborted)
        return credentials;

      WaitUntilCallback(() => { }, () => { return credentials.IsValid; });

      return credentials;
    }

    public void RequestAppInfo(uint appId, bool bForce = false)
    {
      if ((AppInfo.ContainsKey(appId) && !bForce) || bAborted)
        return;

      var completed = false;
      Action<SteamApps.PICSTokensCallback> cbMethodTokens = appTokens =>
      {
        completed = true;
        if (appTokens.AppTokensDenied.Contains(appId))
        {
          Console.WriteLine("Insufficient privileges to get access token for app {0}", appId);
        }

        foreach (var token_dict in appTokens.AppTokens)
        {
          this.AppTokens[token_dict.Key] = token_dict.Value;
        }
      };

      WaitUntilCallback(() =>
      {
        callbacks.Subscribe(steamApps.PICSGetAccessTokens(new List<uint> { appId }, new List<uint>()), cbMethodTokens);
      }, () => { return completed; });

      completed = false;
      Action<SteamApps.PICSProductInfoCallback> cbMethod = appInfo =>
      {
        completed = !appInfo.ResponsePending;

        foreach (var app_value in appInfo.Apps)
        {
          var app = app_value.Value;

          Console.WriteLine("Got AppInfo for {0}", app.ID);
          AppInfo[app.ID] = app;
        }

        foreach (var app in appInfo.UnknownApps)
        {
          AppInfo[app] = null;
        }
      };

      var request = new SteamApps.PICSRequest(appId);
      if (AppTokens.ContainsKey(appId))
      {
        request.AccessToken = AppTokens[appId];
      }

      WaitUntilCallback(() =>
      {
        callbacks.Subscribe(steamApps.PICSGetProductInfo(new List<SteamApps.PICSRequest> { request }, new List<SteamApps.PICSRequest>()), cbMethod);
      }, () => { return completed; });
    }

    public void RequestPackageInfo(IEnumerable<uint> packageIds)
    {
      var packages = packageIds.ToList();
      packages.RemoveAll(pid => PackageInfo.ContainsKey(pid));

      if (packages.Count == 0 || bAborted)
        return;

      var completed = false;
      Action<SteamApps.PICSProductInfoCallback> cbMethod = packageInfo =>
      {
        completed = !packageInfo.ResponsePending;

        foreach (var package_value in packageInfo.Packages)
        {
          var package = package_value.Value;
          PackageInfo[package.ID] = package;
        }

        foreach (var package in packageInfo.UnknownPackages)
        {
          PackageInfo[package] = null;
        }
      };

      var packageRequests = new List<SteamApps.PICSRequest>();

      foreach (var package in packages)
      {
        var request = new SteamApps.PICSRequest(package);

        if (PackageTokens.TryGetValue(package, out var token))
        {
          request.AccessToken = token;
        }

        packageRequests.Add(request);
      }

      WaitUntilCallback(() =>
      {
        callbacks.Subscribe(steamApps.PICSGetProductInfo(new List<SteamApps.PICSRequest>(), packageRequests), cbMethod);
      }, () => { return completed; });
    }

    public bool RequestFreeAppLicense(uint appId)
    {
      var success = false;
      var completed = false;
      Action<SteamApps.FreeLicenseCallback> cbMethod = resultInfo =>
      {
        completed = true;
        success = resultInfo.GrantedApps.Contains(appId);
      };

      WaitUntilCallback(() =>
      {
        callbacks.Subscribe(steamApps.RequestFreeLicense(appId), cbMethod);
      }, () => { return completed; });

      return success;
    }

    public void RequestDepotKey(uint depotId, uint appid = 0)
    {
      if (DepotKeys.ContainsKey(depotId) || bAborted)
        return;

      var completed = false;

      Action<SteamApps.DepotKeyCallback> cbMethod = depotKey =>
      {
        completed = true;
        Console.WriteLine("Got depot key for {0} result: {1}", depotKey.DepotID, depotKey.Result);

        if (depotKey.Result != EResult.OK)
        {
          Abort();
          return;
        }

        DepotKeys[depotKey.DepotID] = depotKey.DepotKey;
      };

      WaitUntilCallback(() =>
      {
        callbacks.Subscribe(steamApps.GetDepotDecryptionKey(depotId, appid), cbMethod);
      }, () => { return completed; });
    }


    public async Task<ulong> GetDepotManifestRequestCodeAsync(uint depotId, uint appId, ulong manifestId, string branch)
    {
      if (bAborted)
        return 0;

      var requestCode = await steamContent.GetManifestRequestCode(depotId, appId, manifestId, branch);

      Console.WriteLine("Got manifest request code for {0} {1} result: {2}",
          depotId, manifestId,
          requestCode);

      return requestCode;
    }

    public void CheckAppBetaPassword(uint appid, string password)
    {
      var completed = false;
      Action<SteamApps.CheckAppBetaPasswordCallback> cbMethod = appPassword =>
      {
        completed = true;

        Console.WriteLine("Retrieved {0} beta keys with result: {1}", appPassword.BetaPasswords.Count, appPassword.Result);

        foreach (var entry in appPassword.BetaPasswords)
        {
          AppBetaPasswords[entry.Key] = entry.Value;
        }
      };

      WaitUntilCallback(() =>
      {
        callbacks.Subscribe(steamApps.CheckAppBetaPassword(appid, password), cbMethod);
      }, () => { return completed; });
    }

    public PublishedFileDetails GetPublishedFileDetails(uint appId, PublishedFileID pubFile)
    {
      var pubFileRequest = new CPublishedFile_GetDetails_Request { appid = appId };
      pubFileRequest.publishedfileids.Add(pubFile);

      var completed = false;
      PublishedFileDetails details = null;

      Action<SteamUnifiedMessages.ServiceMethodResponse> cbMethod = callback =>
      {
        completed = true;
        if (callback.Result == EResult.OK)
        {
          var response = callback.GetDeserializedResponse<CPublishedFile_GetDetails_Response>();
          details = response.publishedfiledetails.FirstOrDefault();
        }
        else
        {
          throw new Exception($"EResult {(int)callback.Result} ({callback.Result}) while retrieving file details for pubfile {pubFile}.");
        }
      };

      WaitUntilCallback(() =>
      {
        callbacks.Subscribe(steamPublishedFile.SendMessage(api => api.GetDetails(pubFileRequest)), cbMethod);
      }, () => { return completed; });

      return details;
    }


    public SteamCloud.UGCDetailsCallback GetUGCDetails(UGCHandle ugcHandle)
    {
      var completed = false;
      SteamCloud.UGCDetailsCallback details = null;

      Action<SteamCloud.UGCDetailsCallback> cbMethod = callback =>
      {
        completed = true;
        if (callback.Result == EResult.OK)
        {
          details = callback;
        }
        else if (callback.Result == EResult.FileNotFound)
        {
          details = null;
        }
        else
        {
          throw new Exception($"EResult {(int)callback.Result} ({callback.Result}) while retrieving UGC details for {ugcHandle}.");
        }
      };

      WaitUntilCallback(() =>
      {
        callbacks.Subscribe(steamCloud.RequestUGCDetails(ugcHandle), cbMethod);
      }, () => { return completed; });

      return details;
    }

    private void ResetConnectionFlags()
    {
      bAborted = false;
      bDidDisconnect = false;
      bDidReceiveLoginKey = false;
    }

    public void Connect()
    {
      bAborted = false;
      bConnected = false;
      bConnecting = true;
      connectionBackoff = 0;

      ResetConnectionFlags();

      this.connectTime = DateTime.Now;
      this.steamClient.Connect();
    }

    private void Abort(bool sendLogOff = true)
    {
      Disconnect(sendLogOff);
    }

    public void Disconnect(bool sendLogOff = true)
    {
      if (sendLogOff)
      {
        steamUser.LogOff();
      }

      bAborted = true;
      bConnected = false;
      bConnecting = false;
      steamClient.Disconnect();

      // flush callbacks until our disconnected event
      while (!bDidDisconnect)
      {
        callbacks.RunWaitAllCallbacks(TimeSpan.FromMilliseconds(100));
      }
    }

    private void Reconnect()
    {
      steamClient.Disconnect();
    }

    public void TryWaitForLoginKey()
    {
      if (ContentDownloader._logonDetails.Username == null || !credentials.LoggedOn || !ContentDownloader.Config.RememberPassword) return;

      var totalWaitPeriod = DateTime.Now.AddSeconds(3);

      while (true)
      {
        var now = DateTime.Now;
        if (now >= totalWaitPeriod) break;

        if (bDidReceiveLoginKey) break;

        callbacks.RunWaitAllCallbacks(TimeSpan.FromMilliseconds(100));
      }
    }

    public void WaitForCallbacks()
    {
      callbacks.RunWaitCallbacks(TimeSpan.FromSeconds(1));

      var diff = DateTime.Now - connectTime;

      if (diff > STEAM3_TIMEOUT && !bConnected)
      {
        Console.WriteLine("Timeout connecting to Steam3.");
        Abort();
      }
    }

    private void ConnectedCallback(SteamClient.ConnectedCallback connected)
    {
      Console.WriteLine(" Done!");
      bConnecting = false;
      bConnected = true;
      if (!authenticatedUser)
      {
        Console.Write("Logging anonymously into Steam3...");
        steamUser.LogOnAnonymous();
      }
      else
      {
        Console.Write("Logging '{0}' into Steam3...", ContentDownloader._logonDetails.Username);
        steamUser.LogOn(ContentDownloader._logonDetails);
      }
    }

    private void DisconnectedCallback(SteamClient.DisconnectedCallback disconnected)
    {
      bDidDisconnect = true;
      //bAborted = true;
      // When recovering the connection, we want to reconnect even if the remote disconnects us
      if (connectionBackoff >= 10)
      {
        Console.WriteLine("Could not connect to Steam after 10 tries");
        Abort(false);
      }
      else if (!bAborted)
      {
        if (bConnecting)
        {
          Console.WriteLine("Connection to Steam failed. Trying again");
        }
        else
        {
          Console.WriteLine("Lost connection to Steam. Reconnecting");
        }

        Thread.Sleep(1000 * ++connectionBackoff);

        // Any connection related flags need to be reset here to match the state after Connect
        ResetConnectionFlags();
        steamClient.Connect();
      }
    }

    private async void LogOnCallback(SteamUser.LoggedOnCallback loggedOn, CoreDelegates core)
    {
      var isSteamGuard = loggedOn.Result == EResult.AccountLogonDenied;
      var is2FA = loggedOn.Result == EResult.AccountLoginDeniedNeedTwoFactor;
      var isLoginKey = ContentDownloader.Config.RememberPassword && ContentDownloader._logonDetails.LoginKey != null && loggedOn.Result == EResult.InvalidPassword;

      if (isSteamGuard || is2FA || isLoginKey)
      {
        Abort(false);

        if (!isLoginKey)
        {
          Console.WriteLine("This account is protected by Steam Guard.");
        }

        if (is2FA)
        {
          try
          {
            if (!credentials.WaitingForAdditionalAuth)
            {
              credentials.WaitingForAdditionalAuth = true;
              ContentDownloader._logonDetails.TwoFactorCode = await core.ui.Request2FA();
              credentials.WaitingForAdditionalAuth = false;
            }
          }
          catch (Exception e)
          {
            credentials.WaitingForAdditionalAuth = false;
            // User canceled
            Abort(true);
            if (e.Message == "task timeout")
            {
              await core.ui.TimedOut(Exec.Parameters.OperationType);
            }
            return;
          }
        }
        else if (isLoginKey)
        {
          AccountSettingsStore.Instance.LoginKeys.Remove(ContentDownloader._logonDetails.Username);
          AccountSettingsStore.Save();

          ContentDownloader._logonDetails.LoginKey = null;

          if (ContentDownloader.Config.SuppliedPassword != null)
          {
            Console.WriteLine("Login key was expired. Connecting with supplied password.");
            ContentDownloader._logonDetails.Password = ContentDownloader.Config.SuppliedPassword;
          }
          else
          {
            Console.Write("Login key was expired. Please enter your password: ");
            string[] creds = await core.ui.RequestCredentials();
            ContentDownloader._logonDetails.Username = creds[0];
            ContentDownloader._logonDetails.Password = creds[1];
          }
        }
        else
        {
          try
          {
            if (!credentials.WaitingForAdditionalAuth)
            {
              credentials.WaitingForAdditionalAuth = true;
              ContentDownloader._logonDetails.AuthCode = await core.ui.RequestSteamGuard();
              credentials.WaitingForAdditionalAuth = false;
            }
          }
          catch (Exception e)
          {
            credentials.WaitingForAdditionalAuth = false;
            // User canceled
            Abort(true);
            if (e.Message == "task timeout")
            {
              await core.ui.TimedOut(Exec.Parameters.OperationType);
            }
            return;
          }
        }
        Console.Write("Retrying Steam3 connection...");
        Reconnect();

        return;
      }

      if (loggedOn.Result == EResult.TryAnotherCM)
      {
        Console.Write("Retrying Steam3 connection (TryAnotherCM)...");

        Reconnect();

        return;
      }

      if (loggedOn.Result == EResult.ServiceUnavailable)
      {
        Console.WriteLine("Unable to login to Steam3: {0}", loggedOn.Result);
        Abort(false);

        return;
      }

      if (eInvalidAuthCode.Contains(loggedOn.Result))
      {
        credentials.WaitingForAdditionalAuth = true;
        ContentDownloader._logonDetails.AuthCode = await core.ui.RequestSteamGuard();
        credentials.WaitingForAdditionalAuth = false;
        Reconnect();
        return;
      }

      if (loggedOn.Result == EResult.RateLimitExceeded)
      {
        await core.ui.RateLimitExceeded();
        Abort(true);
        return;
      }

      if (loggedOn.Result != EResult.OK)
      {
        Console.WriteLine("Unable to login to Steam3: {0}", loggedOn.Result);
        Abort();

        return;
      }

      Console.WriteLine(" Done!");

      this.seq++;
      credentials.LoggedOn = true;

      if (ContentDownloader.Config.CellID == 0)
      {
        Console.WriteLine("Using Steam3 suggested CellID: " + loggedOn.CellID);
        ContentDownloader.Config.CellID = (int)loggedOn.CellID;
      }
    }

    private void SessionTokenCallback(SteamUser.SessionTokenCallback sessionToken)
    {
      Console.WriteLine("Got session token!");
      credentials.SessionToken = sessionToken.SessionToken;
    }

    private void LicenseListCallback(SteamApps.LicenseListCallback licenseList)
    {
      if (licenseList.Result != EResult.OK)
      {
        Console.WriteLine("Unable to get license list: {0} ", licenseList.Result);
        Abort();

        return;
      }

      Console.WriteLine("Got {0} licenses for account!", licenseList.LicenseList.Count);
      Licenses = licenseList.LicenseList;

      foreach (var license in licenseList.LicenseList)
      {
        if (license.AccessToken > 0)
        {
          PackageTokens.TryAdd(license.PackageID, license.AccessToken);
        }
      }
    }

    private void UpdateMachineAuthCallback(SteamUser.UpdateMachineAuthCallback machineAuth)
    {
      var hash = Util.SHAHash(machineAuth.Data);
      Console.WriteLine("Got Machine Auth: {0} {1} {2} {3}", machineAuth.FileName, machineAuth.Offset, machineAuth.BytesToWrite, machineAuth.Data.Length, hash);

      AccountSettingsStore.Instance.SentryData[ContentDownloader._logonDetails.Username] = machineAuth.Data;
      AccountSettingsStore.Save();

      var authResponse = new SteamUser.MachineAuthDetails
      {
        BytesWritten = machineAuth.BytesToWrite,
        FileName = machineAuth.FileName,
        FileSize = machineAuth.BytesToWrite,
        Offset = machineAuth.Offset,

        SentryFileHash = hash, // should be the sha1 hash of the sentry file we just wrote

        OneTimePassword = machineAuth.OneTimePassword, // not sure on this one yet, since we've had no examples of steam using OTPs

        LastError = 0, // result from win32 GetLastError
        Result = EResult.OK, // if everything went okay, otherwise ~who knows~

        JobID = machineAuth.JobID, // so we respond to the correct server job
      };

      // send off our response
      steamUser.SendMachineAuthResponse(authResponse);
    }

    private void LoginKeyCallback(SteamUser.LoginKeyCallback loginKey)
    {
      Console.WriteLine("Accepted new login key for account {0}", ContentDownloader._logonDetails.Username);

      AccountSettingsStore.Instance.LoginKeys[ContentDownloader._logonDetails.Username] = loginKey.LoginKey;
      AccountSettingsStore.Save();

      steamUser.AcceptNewLoginKey(loginKey);

      bDidReceiveLoginKey = true;
    }
  }
}
