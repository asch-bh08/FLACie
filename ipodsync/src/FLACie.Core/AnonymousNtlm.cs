using SMBLibrary.Authentication.GSSAPI;
using SMBLibrary.Authentication.NTLM;
using SMBLibrary.Client.Authentication;

namespace FLACie.Core;

/// <summary>A true anonymous ("null session") NTLM login, which Samba shares with "guest ok = yes" accept. SMBLibrary's own
/// login with an empty user name sends an NTLMv2 response Samba rejects; this sends none, like Windows and jcifs do.</summary>
sealed class AnonymousNtlm : IAuthenticationClient
{
    bool negotiated;

    public byte[] InitializeSecurityContext(byte[] securityBlob)
    {
        if (!negotiated)
        {
            negotiated = true;
            var neg = new NegotiateMessage
            {
                NegotiateFlags = NegotiateFlags.UnicodeEncoding | NegotiateFlags.OEMEncoding | NegotiateFlags.NTLMSessionSecurity | NegotiateFlags.AlwaysSign
                                 | NegotiateFlags.ExtendedSessionSecurity | NegotiateFlags.Use128BitEncryption | NegotiateFlags.Use56BitEncryption | NegotiateFlags.Anonymous,
                Version = NTLMVersion.Server2003,
            };
            return new SimpleProtectedNegotiationTokenInit { MechanismTypeList = [GSSProvider.NTLMSSPIdentifier], MechanismToken = neg.GetBytes() }.GetBytes(true);
        }
        var resp = (SimpleProtectedNegotiationTokenResponse)SimpleProtectedNegotiationToken.ReadToken(securityBlob, 0, false);
        var challenge = new ChallengeMessage(resp.ResponseToken);
        var auth = new AuthenticateMessage
        {
            NegotiateFlags = (challenge.NegotiateFlags & ~NegotiateFlags.Sign) | NegotiateFlags.Anonymous,
            DomainName = "", UserName = "", WorkStation = "", LmChallengeResponse = new byte[1], NtChallengeResponse = [], Version = NTLMVersion.Server2003,
        };
        return new SimpleProtectedNegotiationTokenResponse { ResponseToken = auth.GetBytes() }.GetBytes();
    }

    // an anonymous session has no key; the server doesn't sign it, so an empty one is never used
    public byte[] GetSessionKey() => new byte[16];
    public void ResetSecurityContext(string serverPrincipal) => negotiated = false;
}
