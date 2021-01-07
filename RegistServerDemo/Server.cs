using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RegistServerDemo
{
    public class Server<TRequest, TResponse>
    {
        private readonly IPEndPoint _endpoint;
        private readonly Func<TRequest, TResponse> _processor;
        private readonly TcpListener _listener;

        public Server(IPEndPoint endpoint, Func<TRequest, TResponse> processor)
        {
            _endpoint = endpoint;
            _processor = processor;
            _listener = new TcpListener(_endpoint);
        }

        private void Receive(TcpClient client)
        {
            using (client)
            {
                using (var stream = client.GetStream())
                {

                }
            }
        }
    }
}
