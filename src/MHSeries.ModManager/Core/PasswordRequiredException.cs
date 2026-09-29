namespace MhModManager.Core;

/// <summary>压缩包需要密码(未提供或候选密码全部失败)时抛出,Host 据此驱动前端密码输入流程。</summary>
public sealed class PasswordRequiredException : Exception
{
    public string ArchiveName { get; }

    public PasswordRequiredException(string archiveName, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArchiveName = archiveName;
    }
}
