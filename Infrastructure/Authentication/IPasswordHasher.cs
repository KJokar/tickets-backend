namespace Infrastructure.Authentication;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string passwordHash, string providedPassword);

    bool NeedsRehash(string passwordHash, string providedPassword);
}
