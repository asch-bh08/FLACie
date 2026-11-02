using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>Owner, 2 Nov: a catalog cover found under old lookup rules was served for good, as its key did not change. The rules' version is part of its file name.</summary>
public class CoverStoreRulesTests
{
    [Fact]
    public void A_catalog_covers_file_follows_the_lookup_rules_and_a_Jellyfin_covers_does_not()
    {
        Assert.NotEqual(Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes("itR2VvcmdlIE1pY2hhZWwKCkNhcmVsZXNzIFdoaXNwZXI"))),
            CoverStore.Name("itR2VvcmdlIE1pY2hhZWwKCkNhcmVsZXNzIFdoaXNwZXI"));
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes("jfabc~tag"))), CoverStore.Name("jfabc~tag"));
    }
}
