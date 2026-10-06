using System.Runtime.CompilerServices;
using Server.Persistence;

namespace Server.Tests
{
    internal static class TestAssemblyInit
    {
        // Handler tests assert database state right after a handler ran. Applying write-behind writes inline keeps
        // them deterministic and stops a pending background write from one test leaking into the next test's database.
        // The real asynchronous behaviour is covered by PersistenceServiceTests with their own service instances.
        [ModuleInitializer]
        internal static void UseInlinePersistence() => PersistenceService.Instance.Inline = true;
    }
}
