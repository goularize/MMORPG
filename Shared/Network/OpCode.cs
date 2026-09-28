namespace Shared.Network
{
    public enum OpCode : ushort
    {
        // Internal/System
        Unknown = 0,
        
        // Client -> Server
        SignInRequest = 1,
        SignUpRequest = 3,
        
        // Server -> Client
        SignInResponse = 2,
        SignUpResponse = 4
    }
}
