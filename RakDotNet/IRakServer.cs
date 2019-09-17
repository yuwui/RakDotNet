﻿using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace RakDotNet
{
    public interface IRakServer
    {
        event Func<IPEndPoint, byte[], Reliability, Task> MessageReceived;
        event Func<IPEndPoint, Task> ClientConnected;
        event Func<IPEndPoint, CloseReason, Task> ClientDisconnected;

        Task RunAsync(CancellationToken cancelToken = default);

        Task ShutdownAsync();

        Task SendAsync(IPEndPoint endPoint, byte[] data, Reliability reliability = Reliability.ReliableOrdered);
        
        void Send(IPEndPoint endPoint, byte[] data, Reliability reliability = Reliability.ReliableOrdered);

        Task CloseAsync(IPEndPoint endPoint);

        IRakConnection GetConnection(IPEndPoint endPoint);
    }
}