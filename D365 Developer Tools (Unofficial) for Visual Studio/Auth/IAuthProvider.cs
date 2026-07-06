using System;
using System.Threading.Tasks;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Auth
{
    /// <summary>Acquires access tokens for a single D365/Dataverse connection.</summary>
    internal interface IAuthProvider : IDisposable
    {
        Task<string> GetAccessTokenAsync();
    }
}
