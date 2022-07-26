using Newtonsoft.Json.Linq;

namespace Common
{
  public class ParametersImpl: IParameters
  {
    private string m_userName;
    public string Username { get => m_userName; set => m_userName = value; }

    private string m_password;
    public string Password { get => m_password; set => m_password = value; }

    private bool m_rememberPassword = false;
    public bool RememberPassword { get => m_rememberPassword; }

    private bool m_manifestOnly = false;
    public bool ManifestOnly { get => m_manifestOnly; }

    private int m_cellId = int.MaxValue;
    public int CellId { get => m_cellId; }

    private string m_fileList;
    public string FileList { get => m_fileList; }

    private string m_installDirectory;
    public string InstallDirectory { get => m_installDirectory; }

    private bool m_verifyAll = false;
    public bool VerifyAll { get => m_verifyAll; }

    private int m_maxServers = 20;
    public int MaxServers { get => m_maxServers; }

    private int m_maxDownloads = 8;
    public int MaxDownloads { get => m_maxDownloads; }

    private uint m_loginId;
    public uint LoginId { get => m_loginId; }

    private string m_appId;
    public string AppId { get => m_appId; }

    private ulong m_pubFile;
    public ulong PubFile { get => m_pubFile; }

    private ulong m_ugcId;
    public ulong UgcId { get => m_ugcId; }

    private string m_branch;
    public string Branch { get => m_branch; }

    private string m_betaBranchPassword;
    public string BetaBranchPassword { get => m_betaBranchPassword; }

    private uint[] m_depotIds;
    public uint[] DepotIdList { get => m_depotIds; }

    private ulong[] m_ManifestIdList;
    public ulong[] ManifestIdList { get => m_ManifestIdList; }

    private OpType m_opType;
    public OpType OperationType { get => m_opType; set => m_opType = value; }

    public ParametersImpl(JObject data, OpType opType)
    {
      m_userName = (string?)data[nameof(Username)];
      m_password = (string?)data[nameof(Password)];
      m_rememberPassword = (bool)Util.ValueNormalize<bool>(data[nameof(RememberPassword)]);
      m_manifestOnly = (bool)Util.ValueNormalize<bool>(data[nameof(ManifestOnly)]);
      m_cellId = (int)Util.ValueNormalize<int>(data[nameof(CellId)]);
      m_fileList = (string?)data[nameof(FileList)];
      m_installDirectory = (string?)data[nameof(InstallDirectory)];
      m_verifyAll = (bool)Util.ValueNormalize<bool>(data[nameof(VerifyAll)]);
      m_maxServers = (int)Util.ValueNormalize<int>(data[nameof(MaxServers)], 20);
      m_maxDownloads = (int)Util.ValueNormalize<int>(data[nameof(MaxDownloads)], 8);
      m_loginId = (uint)Util.ValueNormalize<uint>(data[nameof(LoginId)]);
      m_appId = (string?)data[nameof(AppId)];
      m_pubFile = (ulong)Util.ValueNormalize<ulong>(data[nameof(PubFile)], ulong.MaxValue);
      m_ugcId = (ulong)Util.ValueNormalize<ulong>(data[nameof(UgcId)], ulong.MaxValue);
      m_betaBranchPassword = (string?)data[nameof(BetaBranchPassword)];
      m_branch = (string?)data[nameof(Branch)];
      m_depotIds = (data[nameof(DepotIdList)] != null)
        ? data[nameof(DepotIdList)].ToList().Select(input => Convert.ToUInt32(input)).ToArray()
        : null;
      m_opType = opType;

      m_ManifestIdList = (data[nameof(ManifestIdList)] != null)
        ? data[nameof(ManifestIdList)].ToList().Select(input => Convert.ToUInt64(input)).ToArray()
        : null; ;
    }
  }
}
