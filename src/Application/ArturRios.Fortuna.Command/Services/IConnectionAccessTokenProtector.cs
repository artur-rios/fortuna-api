namespace ArturRios.Fortuna.Command.Services;

public interface IConnectionAccessTokenProtector
{
    byte[] Protect(string accessToken);
    string Unprotect(byte[] protectedAccessToken);
}
