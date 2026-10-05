using System.Text;

namespace RemoteHub.Vnc;

internal sealed class VncHandshakeGuard
{
    private readonly byte[] _header = new byte[13];
    private int _count;
    public byte SecurityType { get; private set; }
    public void ValidateOutgoing(ReadOnlySpan<byte> data)
    {
        int count = Math.Min(data.Length, _header.Length - _count);
        data[..count].CopyTo(_header.AsSpan(_count)); _count += count;
        if (_count >= 12 && Encoding.ASCII.GetString(_header, 0, 12) is not ("RFB 003.008\n" or "RFB 003.007\n"))
            throw new InvalidDataException("This viewer requires RFB 3.7/3.8 or macOS Screen Sharing. Legacy RFB 3.3 is not supported.");
        if (_count == 13)
        {
            if (_header[12] is not 2 and not 30)
                throw new InvalidDataException("The server did not select Mac account or VNC password authentication. Passwordless and unsupported security modes are blocked.");
            SecurityType = _header[12];
        }
    }
}
