﻿namespace Common
{
  public enum OpType {
    file_verification,
    mod_download,
  }

  public interface IParameters
  {
    public OpType OperationType { get; set; }
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
    public uint[] DepotIdList { get; }
    public ulong[] ManifestIdList { get; }
  }
}