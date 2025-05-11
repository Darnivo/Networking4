using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace shared {

    /**
     * TcpMessageChannel is sort of a facade around a TcpClient, Packets & the StreamUtil class.
     * It abstracts communication to a single bidirectional channel which you can use to pass 
     * ASerializable objects back and forth, and check to see whether everything is still peachy 
     * where the underlying connection is concerned.
     * 
     * Basically after the initial setup, 'all' you have to worry about is having a channel and
     * being able to push objects through it.
     * 
     * If you want to implement your own serialization mechanism, this is the place to do it.
     */
    public class TcpMessageChannel
    {
        private TcpClient _client = null;                               //the underlying client connection
        private NetworkStream _stream = null;                           //the client's cached stream
        private IPEndPoint _remoteEndPoint = null;                      //cached endpoint info so we can still access it, even if the connection closes
        
        //stores all errors that occurred (can be used for debug info to get an idea of where and why the channel failed)
        private List<Exception> _errors = new List<Exception>();

        //quick cache thingy to avoid reserialization of objects when you have a lot of clients (only applies to the serverside)
        private static ASerializable _lastSerializedMessage = null;
        private static byte[] _lastSerializedBytes = null;
        
        // Last time we successfully sent or received data - used for detecting stale connections
        private DateTime _lastActivity = DateTime.Now;

        /**
         * Creates a TcpMessageChannel based on an existing (and connected) TcpClient.
         * This is usually used on the server side after accepting a TcpClient from a TcpListener.
         */
        public TcpMessageChannel(TcpClient pTcpClient)
        {
            Log.LogInfo("TCPMessageChannel created around "+pTcpClient, this, ConsoleColor.Blue);

            _client = pTcpClient;
            _stream = _client.GetStream();
            _remoteEndPoint = _client.Client.RemoteEndPoint as IPEndPoint;
            _lastActivity = DateTime.Now;
        }

        /**
         * Creates TcpMessageChannel which doesn't have an underlying connected TcpClient yet.
         * This is usually used on the client side, where you call Connect (..,..) on the TcpMessageChannel
         * after creating it. 
         */
        public TcpMessageChannel ()
        {
            Log.LogInfo("TCPMessageChannel created (not connected).", this, ConsoleColor.Blue);
        }

        /**
         * Try to (re)connect to the given server and port (blocks until connected or failed).
         * 
         * @return bool indicating connection status
         */
        public bool Connect (string pServerIP, int pServerPort)
        {
            Log.LogInfo("Connecting...", this, ConsoleColor.Blue);

            try
            {
                _client = new TcpClient();
                _client.Connect(pServerIP, pServerPort);
                _stream = _client.GetStream();
                _remoteEndPoint = _client.Client.RemoteEndPoint as IPEndPoint;
                _errors.Clear();
                _lastActivity = DateTime.Now;
                Log.LogInfo("Connected.", this, ConsoleColor.Blue);
                return true;
            }
            catch (Exception e)
            {
                addError(e);
                return false;
            }
        }

        /**
         * Send the given message through the underlying TcpClient's NetStream.
         */
        public void SendMessage(ASerializable pMessage)
        {
            if (HasErrors())
            {
                Log.LogInfo("This channel has errors, cannot send.", this, ConsoleColor.Red);
                return;
            }

            //everything we log from now to the end of this method should be cyan
            Log.PushForegroundColor(ConsoleColor.Cyan);
            Log.LogInfo(pMessage, this);

            try
            {
                //grab the required bytes from either the packet or the cache
                byte[] bytesToSend;
                
                if (_lastSerializedMessage != pMessage)
                {
                    Packet outPacket = new Packet();
                    outPacket.Write(pMessage);
                    _lastSerializedBytes = outPacket.GetBytes();
                    _lastSerializedMessage = pMessage;
                }
                
                bytesToSend = _lastSerializedBytes;

                // Check if socket is still connected before attempting to write
                if (!IsSocketConnected())
                {
                    throw new Exception("Socket disconnected before sending");
                }

                StreamUtil.Write(_stream, bytesToSend);
                
                // Update activity timestamp
                _lastActivity = DateTime.Now;
            }
            catch (Exception e)
            {
                addError(e);
            }

            Log.PopForegroundColor();
        }

        /**
         * Is there a message pending?
         */
        public bool HasMessage()
        {
            //we use an update StreamUtil.Available check instead of just Available > 0
            return Connected && StreamUtil.Available(_client);
        }

        /**
         * Block until a complete message is read over the underlying's TcpClient's NetStream.
         * If you don't want to block, check HasMessage first().
         */
        public ASerializable ReceiveMessage()
        {
            if (HasErrors())
            {
                Log.LogInfo("This channel has errors, cannot receive.", this, ConsoleColor.Red);
                return null;
            }

            try
            {
                Log.PushForegroundColor(ConsoleColor.Yellow);
                Log.LogInfo("Receiving message...", this);
                
                // Check if socket is still connected before attempting to read
                if (!IsSocketConnected())
                {
                    throw new Exception("Socket disconnected before receiving");
                }
                
                byte[] inBytes = StreamUtil.Read(_stream);
                Packet inPacket = new Packet(inBytes);
                ASerializable inObject = inPacket.ReadObject();
                Log.LogInfo("Received " + inObject, this);
                Log.PopForegroundColor();
                
                // Update activity timestamp
                _lastActivity = DateTime.Now;

                return inObject;
            }
            catch (Exception e)
            {
                addError(e);
                return null;
            }
        }

        /**
         * Similar to TcpClient connected, but also returns false if underlying client is null, or errors were detected.
         */
        public bool Connected 
        {
            get {
                return !HasErrors() && _client != null && _client.Connected && IsSocketConnected();
            }
        }

        public bool HasErrors()
        {
            return _errors.Count > 0;
        }

        public List<Exception> GetErrors()
        {
            return new List<Exception>(_errors);
        }

        private void addError(Exception pError)
        {
            Log.LogInfo("Error added:"+pError, this, ConsoleColor.Red);
            _errors.Add(pError);
            Close();
        }

        public IPEndPoint GetRemoteEndPoint() { return _remoteEndPoint; }

        public void Close()
        {
            try
            {
                if (_client != null && _client.Connected)
                {
                    try
                    {
                        // Try to shutdown socket gracefully
                        _client.Client.Shutdown(SocketShutdown.Both);
                    }
                    catch {}
                    
                    _client.Close();
                }
            } 
            catch {}
            finally
            {
                _client = null;
            }
        }

        public bool IsConnected()
        {
            try
            {
                if (_client == null || !_client.Connected)
                    return false;
                    
                // Check for stale connections (no activity for too long)
                if ((DateTime.Now - _lastActivity).TotalSeconds > 15)
                {
                    // If no activity for 15 seconds, check if socket is still responsive
                    if (!IsSocketAlive())
                    {
                        return false;
                    }
                }
                
                // Additional TCP connection check (most reliable)
                if (!IsSocketConnected())
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
        
        private bool IsSocketConnected()
        {
            try
            {
                if (_client == null || _client.Client == null)
                    return false;
                    
                // This is the most reliable check, but can be expensive
                // Check for socket state without reading data
                return !(_client.Client.Poll(1, SelectMode.SelectRead) && _client.Client.Available == 0);
            }
            catch
            {
                return false;
            }
        }
        
        private bool IsSocketAlive()
        {
            try
            {
                if (_client == null || _client.Client == null)
                    return false;
                    
                // Try to get socket options - will throw if socket is closed
                byte[] optionValue = new byte[4];
                _client.Client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error, optionValue);
                
                // Socket option value should be 0 for a healthy socket
                int errorCode = BitConverter.ToInt32(optionValue, 0);
                if (errorCode != 0)
                {
                    return false;
                }
                
                // Try sending a zero-length packet as a ping
                try {
                    _client.Client.Send(new byte[0], 0, SocketFlags.None);
                    return true;
                }
                catch {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}