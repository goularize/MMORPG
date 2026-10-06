using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Server.Network
{
    /// <summary>
    /// One active session per account. Binding a new connection to an account disconnects the previous one
    /// ("logged in from another location"), which also frees accounts stuck behind a half-open connection that
    /// the server has not noticed is dead. Thread-safe: bound from the connections' read loops.
    /// </summary>
    public static class SessionRegistry
    {
        private static readonly ConcurrentDictionary<int, IClientConnection> _byAccount = new();

        /// <summary>
        /// Makes <paramref name="client"/> the session of <paramref name="accountId"/> and disconnects the session
        /// it replaces. Returns the replaced connection, or null when there was none.
        /// </summary>
        public static IClientConnection? Bind(int accountId, IClientConnection client)
        {
            IClientConnection? previous = null;
            _byAccount.AddOrUpdate(accountId, client, (_, existing) =>
            {
                previous = ReferenceEquals(existing, client) ? null : existing;
                return client;
            });

            if (previous != null)
            {
                Console.WriteLine($"[Session] Account {accountId} signed in again: disconnecting Client {previous.Id} in favour of Client {client.Id}.");
                previous.Disconnect();
            }

            return previous;
        }

        /// <summary>Forgets the session, but only if <paramref name="client"/> is still the registered one.</summary>
        public static void Release(int accountId, IClientConnection client)
        {
            ((ICollection<KeyValuePair<int, IClientConnection>>)_byAccount)
                .Remove(new KeyValuePair<int, IClientConnection>(accountId, client));
        }

        public static IClientConnection? GetSession(int accountId)
            => _byAccount.TryGetValue(accountId, out var client) ? client : null;
    }
}
