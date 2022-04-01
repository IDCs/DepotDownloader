namespace Common
{
  public interface IParameters
  {
    public string Username { get; set; }
    public string Password { get; set; }
    public bool RememberPassword { get; }
    public bool ManifestOnly { get; }
    public int CellId { get; }
    public string FileList { get; }
    public string InstallDirectory { get; }
    public bool VerifyAll { get; }
    public int MaxServers { get; }
    public int MaxDownloads { get; }
    public uint LoginId { get; }
    public string AppId { get; }
    public ulong PubFile { get; }
    public ulong UgcId { get; }
    public string Branch { get; }
    public string BetaBranchPassword { get; }
    public List<uint> DepotIdList { get; }
    public List<ulong> ManifestIdList { get; }
  }
}