/* 
Copyright © 2016 NaturalPoint Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License. 
*/


using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;


namespace NaturalPoint.NatNetLib
{
    [Serializable]
    public class NatNetException : System.Exception
    {
        public NatNetException()
        {
        }


        public NatNetException( string message )
            : base( message )
        {
        }


        public NatNetException( string message, Exception inner )
            : base( message, inner )
        {
        }


        internal static void ThrowIfNotOK( NatNetError result, string message )
        {
            if ( result != NatNetError.NatNetError_OK )
            {
                throw new NatNetException( message + " (" + result.ToString() + ")" );
            }
        }
    }


    internal static class NatNetLogging
    {
        public static event EventHandler<NatNetLogEventArgs> OnLogMessage;

        public class NatNetLogEventArgs : EventArgs
        {
            public NatNetVerbosity Verbosity { get; set; }
            public String Message { get; set; }
        }

        private static NatNetLogCallback m_nativeLogHandler;
        private static NatNetLogEventArgs m_nativeLogEventArgs = new NatNetLogEventArgs();

        static NatNetLogging()
        {
            // This ensures the reverse P/Invoke delegate passed to the native code stays alive.
            m_nativeLogHandler = LogCallbackNativeThunk;

            NatNetLib.NativeMethods.NatNet_SetLogCallback( m_nativeLogHandler );
        }

        /// <summary>
        /// Reverse P/Invoke delegate type for <see cref="NativeMethods.NatNet_SetLogCallback"/>.
        /// </summary>
        /// <param name="level">Log message severity.</param>
        /// <param name="pMessage">Null-terminated char* containing message text.</param>
        private static void LogCallbackNativeThunk( NatNetVerbosity level, IntPtr pMessage )
        {
            try
            {
                if ( OnLogMessage != null )
                {
                    m_nativeLogEventArgs.Verbosity = level;
                    m_nativeLogEventArgs.Message = Marshal.PtrToStringAnsi( pMessage );
                    OnLogMessage( null, m_nativeLogEventArgs );
                }
            }
            catch ( Exception ex )
            {
                // It's important that we consume any exceptions here, since an exception thrown
                // from this reverse P/Invoke delegate would transform into an SEH exception once
                // propagated across the native code boundary, and NatNetLib would blow up.
                System.Diagnostics.Debug.WriteLine( "ERROR - Exception occurred in LogCallbackNativeThunk: " + ex.ToString() );
            }
        }
    }


    internal class NatNetServerDiscovery : IDisposable
    {
        public List<sNatNetDiscoveredServer> DiscoveredServers
        {
            get
            {
                lock ( m_discoveredServers )
                {
                    return new List<sNatNetDiscoveredServer>( m_discoveredServers );
                }
            }
        }
        public event EventHandler<NatNetServerDiscoveredEventArgs> OnServerDiscovered;

        public class NatNetServerDiscoveredEventArgs : EventArgs
        {
            public sNatNetDiscoveredServer DiscoveredServer { get; set; }
        }

        #region Private fields
        private bool m_disposed = false;
        private IntPtr m_discoveryHandle = IntPtr.Zero;
        private NatNetServerDiscoveryCallback m_nativeCallbackHandler;
        private NatNetServerDiscoveredEventArgs m_serverDiscoveredEventArgs = new NatNetServerDiscoveredEventArgs();
        private List<sNatNetDiscoveredServer> m_discoveredServers = new List<sNatNetDiscoveredServer>();
        #endregion Private fields


        public NatNetServerDiscovery( IEnumerable<string> knownServerAddresses = null )
        {
            m_nativeCallbackHandler = ServerDiscoveredNativeThunk;

            if ( knownServerAddresses == null )
            {
                NatNetError result = NatNetLib.NativeMethods.NatNet_CreateAsyncServerDiscovery( out m_discoveryHandle, m_nativeCallbackHandler );
                NatNetException.ThrowIfNotOK( result, "NatNet_CreateAsyncServerDiscovery failed." );
                if ( m_discoveryHandle == IntPtr.Zero )
                {
                    throw new NatNetException( "NatNet_CreateAsyncServerDiscovery returned null handle." );
                }
            }
            else
            {
                NatNetError result = NatNetLib.NativeMethods.NatNet_CreateAsyncServerDiscovery( out m_discoveryHandle, m_nativeCallbackHandler, IntPtr.Zero, false );
                NatNetException.ThrowIfNotOK( result, "NatNet_CreateAsyncServerDiscovery failed." );

                foreach ( string serverAddress in knownServerAddresses )
                {
                    result = NatNetLib.NativeMethods.NatNet_AddDirectServerToAsyncDiscovery( m_discoveryHandle, serverAddress );
                    NatNetException.ThrowIfNotOK( result, "NatNet_AddDirectServerToAsyncDiscovery failed." );
                }

                result = NatNetLib.NativeMethods.NatNet_StartAsyncDiscovery( m_discoveryHandle );
                NatNetException.ThrowIfNotOK( result, "NatNet_StartAsyncDiscovery failed." );
            }
        }


        void ServerDiscoveredNativeThunk( sNatNetDiscoveredServer discoveredServer, IntPtr pUserContext )
        {
            try
            {
                ThrowIfDisposed();

                lock ( m_discoveredServers )
                {
                    m_discoveredServers.Add( discoveredServer );
                }

                if ( OnServerDiscovered != null )
                {
                    m_serverDiscoveredEventArgs.DiscoveredServer = discoveredServer;
                    OnServerDiscovered( this, m_serverDiscoveredEventArgs );
                }
            }
            catch ( Exception ex )
            {
                // It's important that we consume any exceptions here, since an exception thrown
                // from this reverse P/Invoke delegate would transform into an SEH exception once
                // propagated across the native code boundary, and NatNetLib would blow up.
                System.Diagnostics.Debug.WriteLine( "ERROR - Exception occurred in ServerDiscoveredNativeThunk: " + ex.ToString() );
            }
        }


        #region Dispose pattern
        ~NatNetServerDiscovery()
        {
            Dispose( false );
        }


        /// <summary>Implements IDisposable.</summary>
        public void Dispose()
        {
            Dispose( true );
            GC.SuppressFinalize( this );
        }


        /// <summary>
        /// Called by both the <see cref="IDisposable.Dispose()"/> override,
        /// as well as the finalizer, to do the actual cleanup work.
        /// </summary>
        /// <param name="disposing">
        /// True if <see cref="Dispose()"/> was called explicitly. False if
        /// running as part of the finalizer. If false, do not attempt to
        /// reference other managed objects, since they may have already been
        /// finalized themselves.
        /// </param>
        protected virtual void Dispose( bool disposing )
        {
            if ( m_disposed )
                return;

            // Now destroy the native discovery handle.
            NatNetError destroyResult = NatNetLib.NativeMethods.NatNet_FreeAsyncServerDiscovery( m_discoveryHandle );

            if ( destroyResult != NatNetError.NatNetError_OK )
            {
                System.Diagnostics.Debug.WriteLine( "NatNet_FreeAsyncServerDiscovery returned " + destroyResult.ToString() + "." );
            }

            m_discoveryHandle = IntPtr.Zero;

            m_disposed = true;
        }


        private void ThrowIfDisposed()
        {
            if ( m_disposed )
            {
                throw new ObjectDisposedException( GetType().FullName );
            }
        }
        #endregion Dispose pattern
    }


    internal class NatNetClient : IDisposable
    {
        public class DataDescriptions
        {
            public List<sMarkerSetDescription> MarkerSetDescriptions;
            public List<sRigidBodyDescription> RigidBodyDescriptions;
            public List<sSkeletonDescription> SkeletonDescriptions;
            public List<sForcePlateDescription> ForcePlateDescriptions;
        }


        public static Version NatNetLibVersion
        {
            get
            {
                Byte[] natNetLibVersion = new Byte[4];
                NatNetLib.NativeMethods.NatNet_GetVersion( natNetLibVersion );
                return new Version( natNetLibVersion[0], natNetLibVersion[1], natNetLibVersion[2], natNetLibVersion[3] );
            }
        }

        public bool Connected { get; private set; }

        public sServerDescription ServerDescription { get { return m_serverDesc; } }
        public Version ServerAppVersion { get; private set; }

        /// <summary>
        /// This event is raised when a new frame is received via the network.
        /// IMPORTANT: This executes (via reverse P/Invoke) in the context of
        /// the NatNetLib network service thread.
        /// </summary>
        /// <remarks>
        /// NB: The sFrameOfMocapData native structure is large and expensive
        /// to marshal. In particular, each invocation allocates ~200 KB.
        /// </remarks>
        public event EventHandler<NativeFrameReceivedEventArgs> NativeFrameReceived;

        public class NativeFrameReceivedEventArgs : EventArgs
        {
            private sFrameOfMocapData? m_marshaledFrame;
            private IntPtr m_nativeFrame;

            public IntPtr NativeFramePointer {
                get
                {
                    return m_nativeFrame;
                }

                set
                {
                    // Invalidate lazily-evaluated cached marshaled frame.
                    m_marshaledFrame = null;

                    m_nativeFrame = value;
                }
            }

            public sFrameOfMocapData MarshaledFrame {
                get {
                    if ( m_marshaledFrame.HasValue == false )
                    {
                        m_marshaledFrame = (sFrameOfMocapData)Marshal.PtrToStructure( NativeFramePointer, typeof( sFrameOfMocapData ) );
                    }

                    return m_marshaledFrame.Value;
                }
            }
        }


        #region Private fields
        private bool m_disposed = false;
        private IntPtr m_clientHandle = IntPtr.Zero;
        private sServerDescription m_serverDesc;
        private NatNetFrameReceivedCallback m_nativeFrameReceivedHandler;
        private NativeFrameReceivedEventArgs m_nativeFrameReceivedEventArgs = new NativeFrameReceivedEventArgs();
        #endregion Private fields


        public NatNetClient()
        {
            NatNetError retval = NatNetLib.NativeMethods.NatNet_Client_Create( out m_clientHandle );
            NatNetException.ThrowIfNotOK( retval, "NatNet_Client_Create failed." );

            if ( m_clientHandle == IntPtr.Zero )
            {
                throw new NatNetException( "NatNet_Client_Create returned null handle." );
            }

            // This ensures the reverse P/Invoke delegate passed to the native code stays alive.
            m_nativeFrameReceivedHandler = FrameReceivedNativeThunk;

            retval = NatNetLib.NativeMethods.NatNet_Client_SetFrameReceivedCallback( m_clientHandle, m_nativeFrameReceivedHandler );
            NatNetException.ThrowIfNotOK( retval, "NatNet_Client_SetFrameReceivedCallback failed." );
        }


        public void Connect( NatNetConnectionType connType,
                             IPAddress localAddress,
                             IPAddress serverAddress,
                             UInt16 serverCommandPort = NatNetConstants.DefaultCommandPort,
                             UInt16 serverDataPort = NatNetConstants.DefaultDataPort,
                             IPAddress multicastAddress = null )
        {
            ThrowIfDisposed();

            sNatNetClientConnectParams initParams = new sNatNetClientConnectParams {
                ConnectionType = connType,
                ServerCommandPort = serverCommandPort,
                ServerDataPort = serverDataPort,
                LocalAddress = localAddress.ToString(),
                ServerAddress = serverAddress.ToString(),
                MulticastAddress = multicastAddress == null ? null : multicastAddress.ToString()
            };

            NatNetError result;

            result = NatNetLib.NativeMethods.NatNet_Client_Connect( m_clientHandle, ref initParams );
            NatNetException.ThrowIfNotOK( result, "NatNet_Client_Connect failed." );

            result = NatNetLib.NativeMethods.NatNet_Client_GetServerDescription( m_clientHandle, out m_serverDesc );
            NatNetException.ThrowIfNotOK( result, "NatNet_Client_GetServerDescription failed." );

            ServerAppVersion = new Version(
                m_serverDesc.HostAppVersion[0],
                m_serverDesc.HostAppVersion[1],
                m_serverDesc.HostAppVersion[2],
                m_serverDesc.HostAppVersion[3] );

            Connected = true;
        }


        public void Disconnect()
        {
            ThrowIfDisposed();

            if ( Connected )
            {
                NatNetError retval = NatNetLib.NativeMethods.NatNet_Client_Disconnect( m_clientHandle );
                NatNetException.ThrowIfNotOK( retval, "NatNet_Client_Disconnect failed." );

                Connected = false;
            }
        }

        public NatNetError Request( string request, out IntPtr pResponse, out Int32 pResponseLenBytes, Int32 timeoutMs = 1000, Int32 numAttempts = 1 )
        {
            ThrowIfDisposed();

            NatNetError retval = NatNetLib.NativeMethods.NatNet_Client_Request( m_clientHandle, request, out pResponse, out pResponseLenBytes, timeoutMs, numAttempts );
            NatNetException.ThrowIfNotOK( retval, "NatNet_Client_Request failed." );

            return retval;
        }


        public float RequestFloat( string request, Int32 timeoutMs = 1000, Int32 numAttempts = 1 )
        {
            ThrowIfDisposed();

            IntPtr responsePtr;
            Int32 responseLen;
            NatNetError result = NatNetLib.NativeMethods.NatNet_Client_Request( m_clientHandle, request, out responsePtr, out responseLen, timeoutMs, numAttempts );
            NatNetException.ThrowIfNotOK( result, "NatNet_Client_Request failed.");

            if ( responseLen != Marshal.SizeOf( typeof(float) ) )
            {
                throw new NatNetException( "Response has incorrect length" );
            }

            float[] responseArray = { float.NaN };
            Marshal.Copy( responsePtr, responseArray, 0, 1 );
            return responseArray[0];
        }


        public Int32 RequestInt32( string request, Int32 timeoutMs = 1000, Int32 numAttempts = 1 )
        {
            ThrowIfDisposed();

            IntPtr responsePtr;
            Int32 responseLen;
            NatNetError result = NatNetLib.NativeMethods.NatNet_Client_Request( m_clientHandle, request, out responsePtr, out responseLen, timeoutMs, numAttempts );
            NatNetException.ThrowIfNotOK( result, "NatNet_Client_Request failed." );

            if ( responseLen != Marshal.SizeOf( typeof(Int32) ) )
            {
               throw new NatNetException( "Response has incorrect length" );
            }

            Int32[] responseArray = { Int32.MinValue };
            Marshal.Copy( responsePtr, responseArray, 0, 1 );
            return responseArray[0];
        }


        public bool RequestCommand( string request, Int32 timeoutMs = 1000, Int32 numAttempts = 1 )
        {
            ThrowIfDisposed();

            IntPtr responsePtr;
            Int32 responseLen;
            NatNetError result = NatNetLib.NativeMethods.NatNet_Client_Request(m_clientHandle, request, out responsePtr, out responseLen, timeoutMs, numAttempts);
            return result == NatNetError.NatNetError_OK;
        }


        public DataDescriptions GetDataDescriptions( UInt32 descriptionTypesMask = 0xFFFFFFFF )
        {
            ThrowIfDisposed();

            IntPtr pDataDescriptions;
            NatNetError retval = NatNetLib.NativeMethods.NatNet_Client_GetDataDescriptionList( m_clientHandle, out pDataDescriptions, descriptionTypesMask );
            NatNetException.ThrowIfNotOK( retval, "NatNet_Client_GetDataDescriptions failed." );

            sDataDescriptions dataDescriptions = (sDataDescriptions)Marshal.PtrToStructure( pDataDescriptions, typeof( sDataDescriptions ) );

            // Do a quick first pass to determine the required capacity for the returned lists.
            Int32 numMarkerSetDescs = 0;
            Int32 numRigidBodyDescs = 0;
            Int32 numSkeletonDescs = 0;
            Int32 numForcePlateDescs = 0;

            for ( Int32 i = 0; i < dataDescriptions.DataDescriptionCount; ++i )
            {
                sDataDescription desc = dataDescriptions.DataDescriptions[i];

                switch ( desc.DescriptionType )
                {
                    case (Int32)NatNetDataDescriptionType.NatNetDataDescriptionType_MarkerSet:
                        ++numMarkerSetDescs;
                        break;
                    case (Int32)NatNetDataDescriptionType.NatNetDataDescriptionType_RigidBody:
                        ++numRigidBodyDescs;
                        break;
                    case (Int32)NatNetDataDescriptionType.NatNetDataDescriptionType_Skeleton:
                        ++numSkeletonDescs;
                        break;
                    case (Int32)NatNetDataDescriptionType.NatNetDataDescriptionType_ForcePlate:
                        ++numForcePlateDescs;
                        break;
                }
            }

            // Allocate the lists to be returned based on our counts.
            DataDescriptions retDescriptions = new DataDescriptions {
                MarkerSetDescriptions = new List<sMarkerSetDescription>( numMarkerSetDescs ),
                RigidBodyDescriptions = new List<sRigidBodyDescription>( numRigidBodyDescs ),
                SkeletonDescriptions = new List<sSkeletonDescription>( numSkeletonDescs ),
                ForcePlateDescriptions = new List<sForcePlateDescription>( numForcePlateDescs ),
            };

            // Now populate the lists.
            for ( Int32 i = 0; i < dataDescriptions.DataDescriptionCount; ++i )
            {
                sDataDescription desc = dataDescriptions.DataDescriptions[i];

                switch ( desc.DescriptionType )
                {
                    case (Int32)NatNetDataDescriptionType.NatNetDataDescriptionType_MarkerSet:
                        sMarkerSetDescription markerSetDesc = (sMarkerSetDescription)Marshal.PtrToStructure( desc.Description, typeof( sMarkerSetDescription ) );
                        retDescriptions.MarkerSetDescriptions.Add( markerSetDesc );
                        break;
                    case (Int32)NatNetDataDescriptionType.NatNetDataDescriptionType_RigidBody:
                        sRigidBodyDescription rigidBodyDesc = (sRigidBodyDescription)Marshal.PtrToStructure( desc.Description, typeof( sRigidBodyDescription ) );
                        retDescriptions.RigidBodyDescriptions.Add( rigidBodyDesc );
                        break;
                    case (Int32)NatNetDataDescriptionType.NatNetDataDescriptionType_Skeleton:
                        sSkeletonDescription skeletonDesc = (sSkeletonDescription)Marshal.PtrToStructure( desc.Description, typeof( sSkeletonDescription ) );
                        retDescriptions.SkeletonDescriptions.Add( skeletonDesc );
                        break;
                    case (Int32)NatNetDataDescriptionType.NatNetDataDescriptionType_ForcePlate:
                        sForcePlateDescription forcePlateDesc = (sForcePlateDescription)Marshal.PtrToStructure( desc.Description, typeof( sForcePlateDescription ) );
                        retDescriptions.ForcePlateDescriptions.Add( forcePlateDesc );
                        break;
                }
            }

            return retDescriptions;
        }


        public NatNetError GetPredictedRigidBodyPose( Int32 rbId, out sRigidBodyData rbData, double dt )
        {
            return NatNetLib.NativeMethods.NatNet_Client_GetPredictedRigidBodyPose( m_clientHandle, rbId, out rbData, dt );
        }


        /// <summary>
        /// Reverse P/Invoke delegate passed to <see cref="NativeMethods.NatNet_Client_SetFrameReceivedCallback"/>.
        /// </summary>
        /// <param name="pFrameOfMocapData">Native pointer to a <see cref="sFrameOfMocapData"/> struct.</param>
        /// <param name="pUserData">Native user-provided callback context pointer (void*).</param>
        private void FrameReceivedNativeThunk( IntPtr pFrameOfMocapData, IntPtr pUserData )
        {
            try
            {
                ThrowIfDisposed();

                if ( NativeFrameReceived != null )
                {
                    m_nativeFrameReceivedEventArgs.NativeFramePointer = pFrameOfMocapData;
                    NativeFrameReceived( this, m_nativeFrameReceivedEventArgs );
                }
            }
            catch ( Exception ex )
            {
                // It's important that we consume any exceptions here, since an exception thrown
                // from this reverse P/Invoke delegate would transform into an SEH exception once
                // propagated across the native code boundary, and NatNetLib would blow up.
                System.Diagnostics.Debug.WriteLine( "ERROR - Exception occurred in FrameReceivedNativeThunk: " + ex.ToString() );
            }
        }


        #region Dispose pattern
        ~NatNetClient()
        {
            Dispose( false );
        }


        /// <summary>Implements IDisposable.</summary>
        public void Dispose()
        {
            Dispose( true );
            GC.SuppressFinalize( this );
        }


        /// <summary>
        /// Called by both the <see cref="IDisposable.Dispose()"/> override,
        /// as well as the finalizer, to do the actual cleanup work.
        /// </summary>
        /// <param name="disposing">
        /// True if <see cref="Dispose()"/> was called explicitly. False if
        /// running as part of the finalizer. If false, do not attempt to
        /// reference other managed objects, since they may have already been
        /// finalized themselves.
        /// </param>
        protected virtual void Dispose( bool disposing )
        {
            if ( m_disposed )
                return;

            // Disconnect if necessary.
            if ( Connected )
            {
                NatNetError disconnectResult = NatNetLib.NativeMethods.NatNet_Client_Disconnect( m_clientHandle );

                if ( disconnectResult != NatNetError.NatNetError_OK )
                {
                    System.Diagnostics.Debug.WriteLine( "NatNet_Client_Disconnect returned " + disconnectResult.ToString() + "." );
                }

                Connected = false;
            }

            // Now destroy the native client.
            NatNetError destroyResult = NatNetLib.NativeMethods.NatNet_Client_Destroy( m_clientHandle );

            if ( destroyResult != NatNetError.NatNetError_OK )
            {
                System.Diagnostics.Debug.WriteLine( "NatNet_Client_Destroy returned " + destroyResult.ToString() + "." );
            }

            m_clientHandle = IntPtr.Zero;

            m_disposed = true;
        }


        private void ThrowIfDisposed()
        {
            if ( m_disposed )
            {
                throw new ObjectDisposedException( GetType().FullName );
            }
        }
        #endregion Dispose pattern
    }
}
