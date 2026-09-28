namespace Shared.Network
{
    public enum OpCode : ushort
    {
        // Internal/System
        Unknown = 0,
        
        // Client -> Server
        LoginRequest = 1,
        SignUpRequest = 3,
        
        // Server -> Client
        LoginResponse = 2,
        SignUpResponse = 4
    }
}
