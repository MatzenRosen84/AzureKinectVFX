/* 
Copyright � 2016 NaturalPoint Inc.

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
using System.Runtime.InteropServices;

using NaturalPoint.NatNetLib;


// NOTE: These native structure representations are in some places incomplete
// (e.g. IntPtr to unspecified data) or untested (e.g. the force plate data
// types have not received any testing). See NatNetTypes.h for more info.
//
// This code may be unstable and is subject to change.

namespace NaturalPoint.NatNetLib
{
    internal static class NatNetConstants
    {
        public const string NatNetLibDllBaseName = "NatNetLib";
        public const CallingConvention NatNetLibCallingConvention = CallingConvention.Cdecl;

        public const int MaxModels = 2000;
        public const int MaxMarkerSets = 1000;
        public const int MaxRigidBodies = 1000;
        public const int MaxNameLength = 256;
        public const int MaxMarkers = 200;
        public const int MaxRbMarkers = 20;
        public const int MaxSkeletons = 100;
        public const int MaxSkeletonRigidBodies = 200;
        public const int MaxLabeledMarkers = 1000;
        public const int MaxUnlabeledMarkers = 1000;
        public const int MaxForcePlates = 32;
        public const int MaxDevices = 32;
        public const int MaxAnalogChannels = 32;
        public const int MaxAnalogSubframes = 30;

        public const UInt16 DefaultCommandPort = 1510;
        public const UInt16 DefaultDataPort = 1511;

        public const int Ipv4AddrStrLenMax = 16;
    }


    #region Enumerations
    internal enum NatNetError
    {
        NatNetError_OK = 0,
        NatNetError_Internal,
        NatNetError_External,
        NatNetError_Network,
        NatNetError_Other,
        NatNetError_InvalidArgument,
        NatNetError_InvalidOperation
    }


    internal enum NatNetConnectionType
    {
        NatNetConnectionType_Multicast = 0,
        NatNetConnectionType_Unicast
    }


    internal enum NatNetDataDescriptionType
    {
        NatNetDataDescriptionType_MarkerSet = 0,
        NatNetDataDescriptionType_RigidBody,
        NatNetDataDescriptionType_Skeleton,
        NatNetDataDescriptionType_ForcePlate,
        NatNetDataDescriptionType_Device,
        NatNetDataDescriptionType_Camera,
    };


    internal enum NatNetVerbosity
    {
        None = 0,
        Debug,
        Info,
        Warning,
        Error,
    };
    #endregion Enumerations


    #region Definition types
    [StructLayout( LayoutKind.Sequential )]
    internal struct MarkerDataVector
    {
        [MarshalAs( UnmanagedType.ByValArray, SizeConst = 3 )]
        public float[] Values;
    }


    [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Ansi )]
    internal struct sServerDescription
    {
        [MarshalAs( UnmanagedType.U1 )]
        public bool HostPresent;

        [MarshalAs( UnmanagedType.ByValTStr, SizeConst = NatNetConstants.MaxNameLength )]
        public string HostComputerName;

        [MarshalAs( UnmanagedType.ByValArray, SizeConst = 4 )]
        public byte[] HostComputerAddress;

        [MarshalAs( UnmanagedType.ByValTStr, SizeConst = NatNetConstants.MaxNameLength )]
        public string HostApp;

        [MarshalAs( UnmanagedType.ByValArray, SizeConst = 4 )]
        public byte[] HostAppVersion;

        [MarshalAs( UnmanagedType.ByValArray, SizeConst = 4 )]
        public byte[] NatNetVersion;

        public UInt64 HighResClockFrequency;

        [MarshalAs( UnmanagedType.U1 )]
        public bool ConnectionInfoValid;

        public UInt16 ConnectionDataPort;

        [MarshalAs( UnmanagedType.U1 )]
        public bool ConnectionMulticast;

        [MarshalAs( UnmanagedType.ByValArray, SizeConst = 4 )]
        public byte[] ConnectionMulticastAddress;
    }


    [StructLayout( LayoutKind.Sequential )]
    internal struct sDataDescriptions
    {
        public Int32 DataDescriptionCount;

        [MarshalAs( UnmanagedType.ByValArray, SizeConst = NatNetConstants.MaxModels )]
        public sDataDescription[] DataDescriptions;
    }


    [StructLayout( LayoutKind.Sequential )]
    internal struct sDataDescription
    {
        public Int32 DescriptionType;
        public IntPtr Description;
    }


    [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Ansi )]
    internal struct sMarkerSetDescription
    {
        [MarshalAs( UnmanagedType.ByValTStr, SizeConst = NatNetConstants.MaxNameLength )]
        public string Name;

        public Int32 MarkerCount;
        public IntPtr MarkerNames; // char**, "array of marker names"
    }


    [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Ansi )]
    internal struct sRigidBodyDescription
    {
        [MarshalAs( UnmanagedType.ByValTStr, SizeConst = NatNetConstants.MaxNameLength )]
        public string Name;

        public Int32 Id;
        public Int32 ParentId;
        public float OffsetX;
        public float OffsetY;
        public float OffsetZ;
        public Int32 MarkerCount;
        public IntPtr MarkerPositions; // Pointer to float[MarkerCount][3]
        public IntPtr MarkerRequiredLabels; // Pointer to int32_t[MarkerCount]
        public IntPtr MarkerNames; // char**, "array of marker names"
    }


    [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Ansi )]
    internal struct sSkeletonDescription
    {
        [MarshalAs( UnmanagedType.ByValTStr, SizeConst = NatNetConstants.MaxNameLength )]
        public string Name;

        public Int32 Id;
        public Int32 RigidBodyCount;

        [MarshalAs( UnmanagedType.ByValArray, SizeConst = NatNetConstants.MaxSkeletonRigidBodies )]
        public sRigidBodyDescription[] RigidBodies;
    }


    // Marshalling helper for char[][] ChannelNames in sForcePlateDescription/sDeviceDescription.
    [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Ansi )]
    internal struct sChannelName
    {
        [MarshalAs( UnmanagedType.ByValTStr, SizeConst = NatNetConstants.MaxNameLength )]
        private string Value;

        public static implicit operator string( sChannelName source )
        {
            return source.Value;
        }

        public static implicit operator sChannelName( string source )
        {
            // Note that longer strings would be silently truncated if we didn't explicitly check this here.
            if ( source.Length >= NatNetConstants.MaxNameLength )
                throw new ArgumentException( "String too large for field: \"" + source + "\"" );

            return new sChannelName { Value = source };
        }
    }


    [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Ansi )]
    internal struct sForcePlateDescription
    {
        public Int32 Id;

        [MarshalAs( UnmanagedType.ByValTStr, SizeConst = 128 )]
        public string SerialNo;

        public float Width;
        public float Length;
        public float OriginX;
        public float OriginY;
        public float OriginZ;

        [MarshalAs( UnmanagedType.ByValArray, SizeConst = 12*12 )]
        public float[] CalibrationMatrix;

        [MarshalAs( UnmanagedType.ByValArray, SizeConst = 4*3 )]
        public float[] Corners;

        public Int32 PlateType;
        public Int32 ChannelDataType;
        public Int32 ChannelCount;

        [MarshalAs( UnmanagedType.ByValArray, SizeConst = NatNetConstants.MaxAnalogChannels )]
        public sChannelName[] ChannelNames;
    }


    [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Ansi )]
    internal struct sDeviceDescription
    {
        public Int32 Id;

        [MarshalAs( UnmanagedType.ByValTStr, SizeConst = 128 )]
        public string Name;

        [MarshalAs( UnmanagedType.ByValTStr, SizeConst = 128 )]
        public string SerialNo;

        public Int32 DeviceType;
        public Int32 ChannelDataType;
        public Int32 ChannelCount;

        [MarshalAs( UnmanagedType.ByValArray, SizeConst = NatNetConstants.MaxAnalogChannels )]
        public sChannelName[] ChannelNames;
    }
    #endregion Definition types


    #region Data types
    [StructLayout( LayoutKind.Sequential )]
    internal struct sFrameOfMocapData
    {
        public Int32 FrameNumber;

        public Int32 MarkerSetCount;
        [MarshalAs( UnmanagedType.ByValArray, SizeConst = NatNetConstants.MaxModels )]
        public sMarkerSetData[] MarkerSets;

        public Int32 OtherMarkerCount;
        public IntPtr OtherMarkers; // Pointer to float[OtherMarkerCount][3]

        public Int32 RigidBodyCount;
        [MarshalAs( UnmanagedType.ByValArray, SizeConst = NatNetConstants.MaxRigidBodies )]
        public sRigidBodyData[] RigidBodies;

        public Int32 SkeletonCount;
        [MarshalAs( UnmanagedType.ByValArray, SizeConst = NatNetConstants.MaxSkeletons )]
        public sSkeletonData[] Skeletons;

        public Int32 LabeledMarkerCount;
        [MarshalAs( UnmanagedType.ByValArray, SizeConst = NatNetConstants.MaxLabeledMarkers )]
        public sMarker[] LabeledMarkers;

        public Int32 ForcePlateCount;
        [MarshalAs( UnmanagedType.ByValArray, SizeConst = NatNetConstants.MaxForcePlates )]
        public sForcePlateData[] ForcePlates;

        public Int32 DeviceCount;
        [MarshalAs( UnmanagedType.ByValArray, SizeConst = NatNetConstants.MaxDevices )]
        public sDeviceData[] Devices;

        public UInt32 Timecode;
        public UInt32 TimecodeSubframe;
        public double Timestamp;
        public UInt64 CameraMidExposureTimestamp;
        public UInt64 CameraDataReceivedTimestamp;
        public UInt64 TransmitTimestamp;
        public Int16 Params;
    }


    [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Ansi )]
    internal struct sMarkerSetData
    {
        [MarshalAs( UnmanagedType.ByValTStr, SizeConst = NatNetConstants.MaxNameLength )]
        public string Name;

        public Int32 MarkerCount;
        public IntPtr Markers; // Pointer to float[MarkerCount][3]
    }


    [StructLayout( LayoutKind.Sequential )]
    internal struct sRigidBodyData
    {
        public Int32 Id;
        public float X;
        public float Y;
        public float Z;
        public float QX;
        public float QY;
        public float QZ;
        public float QW;
        public float MeanError;
        public Int16 Params;
    }


    [StructLayout( LayoutKind.Sequential )]
    internal struct sSkeletonData
    {
        public Int32 Id;
        public Int32 RigidBodyCount;
        public IntPtr RigidBodies; // Pointer to sRigidBodyData[RigidBodyCount]
    }


    [StructLayout( LayoutKind.Sequential )]
    internal struct sMarker
    {
        public Int32 Id;
        public float X;
        public float Y;
        public float Z;
        public float Size;
        public Int16 Params;
    }


    [StructLayout( LayoutKind.Sequential )]
    internal struct sAnalogChannelData
    {
        public Int32 FrameCount;
        [MarshalAs( UnmanagedType.ByValArray, SizeConst = NatNetConstants.MaxAnalogSubframes )]
        public float[] Values;
    }


    [StructLayout( LayoutKind.Sequential )]
    internal struct sForcePlateData
    {
        public Int32 Id;
        public Int32 ChannelCount;
        [MarshalAs( UnmanagedType.ByValArray, SizeConst = NatNetConstants.MaxAnalogChannels )]
        public sAnalogChannelData[] ChannelData;
        public Int16 Params;
    }


    [StructLayout( LayoutKind.Sequential )]
    internal struct sDeviceData
    {
        public Int32 Id;
        public Int32 ChannelCount;
        [MarshalAs( UnmanagedType.ByValArray, SizeConst = NatNetConstants.MaxAnalogChannels )]
        public sAnalogChannelData[] ChannelData;
        public Int16 Params;

    }


    [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Ansi )]
    internal struct sNatNetClientConnectParams
    {
        public NatNetConnectionType ConnectionType;
        public UInt16 ServerCommandPort;
        public UInt16 ServerDataPort;
        public string ServerAddress;
        public string LocalAddress;
        public string MulticastAddress;
    };


    [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Ansi )]
    internal struct sNatNetDiscoveredServer
    {
        [MarshalAs( UnmanagedType.ByValTStr, SizeConst = NatNetConstants.Ipv4AddrStrLenMax )]
        public string LocalAddress;
        [MarshalAs( UnmanagedType.ByValTStr, SizeConst = NatNetConstants.Ipv4AddrStrLenMax )]
        public string ServerAddress;
        public UInt16 ServerCommandPort;
        public sServerDescription ServerDescription;
    }
    #endregion Data types


    /// <summary>
    /// Reverse P/Invoke delegate type for <see cref="NativeMethods.NatNet_Client_SetFrameReceivedCallback"/>.
    /// </summary>
    /// <param name="pFrameOfMocapData">Native pointer to <see cref="sFrameOfMocapData"/>.</param>
    /// <param name="pUserData">User-provided context (void pointer).</param>
    [UnmanagedFunctionPointer( NatNetConstants.NatNetLibCallingConvention )]
    internal delegate void NatNetFrameReceivedCallback( IntPtr pFrameOfMocapData, IntPtr pUserData );


    /// <summary>
    /// Reverse P/Invoke delegate type for <see cref="NativeMethods.NatNet_SetLogCallback"/>.
    /// </summary>
    /// <param name="level">Log message severity.</param>
    /// <param name="pMessage">Null-terminated char* containing message text.</param>
    [UnmanagedFunctionPointer( NatNetConstants.NatNetLibCallingConvention )]
    internal delegate void NatNetLogCallback( NatNetVerbosity level, IntPtr pMessage );


    /// <summary>
    /// Reverse P/Invoke delegate type for <see cref="NativeMethods.NatNet_CreateAsyncServerDiscovery"/>.
    /// </summary>
    /// <param name="level">Log message severity.</param>
    /// <param name="pMessage">Null-terminated char* containing message text.</param>
    [UnmanagedFunctionPointer( NatNetConstants.NatNetLibCallingConvention )]
    internal delegate void NatNetServerDiscoveryCallback( sNatNetDiscoveredServer discoveredServer, IntPtr pUserContext );


    internal static class NativeMethods
    {
        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern void NatNet_GetVersion( [In, Out, MarshalAs( UnmanagedType.LPArray, SizeConst=4 )] byte[] version );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern void NatNet_DecodeID( Int32 compositeId, out Int32 entityId, out Int32 memberId );

		[DllImport(NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention)]
        public static extern void NatNet_DecodeTimecode(UInt32 compositeId, UInt32 timecodeSubframe, out Int32 pOutHour, out Int32 pOutMinute, out Int32 pOutSecond, out Int32 pOutFrame, out Int32 pOutSubFrame);											 


        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern void NatNet_SetLogCallback( NatNetLogCallback pfnCallback );


        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_CreateAsyncServerDiscovery( out IntPtr discoveryHandle, NatNetServerDiscoveryCallback pfnCallback, IntPtr pUserContext = default(IntPtr), [MarshalAs( UnmanagedType.U1 )] bool startImmediately = true );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true )]
        public static extern NatNetError NatNet_AddDirectServerToAsyncDiscovery( IntPtr discoveryHandle, string serverAddress );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_StartAsyncDiscovery( IntPtr discoveryHandle );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_FreeAsyncServerDiscovery( IntPtr discoveryHandle );


        //////////////////////////////////////////////////////////////////////
        // These functions are not a supported part of the public API, and are
        // subject to change without notice.

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Client_Create( out IntPtr clientHandle );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Client_Destroy( IntPtr clientHandle );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Client_Connect( IntPtr clientHandle, ref sNatNetClientConnectParams connectParams );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Client_Disconnect( IntPtr clientHandle );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Client_SetFrameReceivedCallback( IntPtr clientHandle, NatNetFrameReceivedCallback pfnDataCallback );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true )]
        public static extern NatNetError NatNet_Client_Request( IntPtr clientHandle, string request, out IntPtr pResponse, out Int32 pResponseLenBytes, Int32 timeoutMs = 1000, Int32 numAttempts = 1 );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Client_GetServerDescription( IntPtr clientHandle, out sServerDescription serverDescription );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Client_GetDataDescriptionList( IntPtr clientHandle, out IntPtr pDataDescriptions, UInt32 descriptionTypesMask = 0xFFFFFFFF );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Client_SecondsSinceHostTimestamp( IntPtr clientHandle, UInt64 inTimestamp, out double pOutTimeElapsed );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Client_GetPredictedRigidBodyPose( IntPtr client, Int32 rigidBodyIndex, out sRigidBodyData rigidBodyData, double dt );

        [DllImport(NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention)]
        public static extern NatNetError NatNet_Frame_GetTimecode(IntPtr pFrameOfMocapData, out UInt32 timecode, out UInt32 timecodeSubframe);		
		
        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Frame_GetRigidBodyCount( IntPtr pFrameOfMocapData, out Int32 rigidBodyCount );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Frame_GetRigidBody( IntPtr pFrameOfMocapData, Int32 rigidBodyIndex, out sRigidBodyData rigidBodyData );


        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Frame_GetSkeletonCount( IntPtr pFrameOfMocapData, out Int32 skeletonCount );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Frame_Skeleton_GetId( IntPtr pFrameOfMocapData, Int32 skeletonIndex, out Int32 skeletonId );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Frame_Skeleton_GetRigidBodyCount( IntPtr pFrameOfMocapData, Int32 skeletonIndex, out Int32 rigidBodyCount );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Frame_Skeleton_GetRigidBody( IntPtr pFrameOfMocapData, Int32 skeletonIndex, Int32 rigidBodyIndex, out sRigidBodyData rigidBodyData );


        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Frame_GetTransmitTimestamp( IntPtr pFrameOfMocapData, out UInt64 pOutTransmitTimestamp );


        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Frame_GetLabeledMarkerCount( IntPtr pFrameOfMocapData, out Int32 labeledMarkerCount );

        [DllImport( NatNetConstants.NatNetLibDllBaseName, CallingConvention = NatNetConstants.NatNetLibCallingConvention )]
        public static extern NatNetError NatNet_Frame_GetLabeledMarker( IntPtr pFrameOfMocapData, Int32 labeledMarkerIndex, out sMarker labeledMarkerData );
    }
}
