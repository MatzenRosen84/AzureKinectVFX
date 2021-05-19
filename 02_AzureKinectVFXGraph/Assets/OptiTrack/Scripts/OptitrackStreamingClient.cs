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
using System.Threading;
using UnityEngine;
using NaturalPoint;
using NaturalPoint.NatNetLib;


/// <summary>Skeleton naming conventions supported by OptiTrack Motive.</summary>
public enum OptitrackBoneNameConvention
{
    Motive,
    FBX,
    BVH,
}


/// <summary>Describes the position and orientation of a streamed tracked object.</summary>
public class OptitrackPose
{
    public Vector3 Position;
    public Quaternion Orientation;
}



/// <summary>Represents the state of a streamed marker.</summary>
public class OptitrackMarkerState
{
    public Vector3 Position;
    public float Size;
    public bool Labeled;
    public Int32 Id;
}


/// <summary>Represents the state of a streamed rigid body at an instant in time.</summary>
public class OptitrackRigidBodyState
{
    public OptitrackHiResTimer.Timestamp DeliveryTimestamp;
    public OptitrackPose Pose;
}


/// <summary>Represents the state of a streamed skeleton at an instant in time.</summary>
public class OptitrackSkeletonState
{
    /// <summary>Maps from OptiTrack bone IDs to their corresponding bone poses.</summary>
    public Dictionary<Int32, OptitrackPose> BonePoses;
    public Dictionary<Int32, OptitrackPose> LocalBonePoses;
}


public class OptitrackRigidBodyDefinition
{
    public class MarkerDefinition
    {
        public Vector3 Position;
        public Int32 RequiredLabel;
    }

    public Int32 Id;
    public string Name;
    public List<MarkerDefinition> Markers;
}

/// <summary>Describes the hierarchy and neutral pose of a streamed skeleton.</summary>
public class OptitrackSkeletonDefinition
{
    public class BoneDefinition
    {
        /// <summary>The ID of this bone within this skeleton.</summary>
        public Int32 Id;

        /// <summary>The ID of this bone's parent bone. A value of 0 means that this is the root bone.</summary>
        public Int32 ParentId;

        /// <summary>The name of this bone.</summary>
        public string Name;

        /// <summary>
        /// This bone's position offset from its parent in the skeleton's neutral pose.
        /// (The neutral orientation is always <see cref="Quaternion.identity"/>.)
        /// </summary>
        public Vector3 Offset;
    }

    /// <summary>Skeleton ID. Used as an argument to <see cref="OptitrackStreamingClient.GetLatestSkeletonState"/>.</summary>
    public Int32 Id;

    /// <summary>Skeleton asset name.</summary>
    public string Name;

    /// <summary>Bone names, hierarchy, and neutral pose position information.</summary>
    public List<BoneDefinition> Bones;

    /// <summary>Bone hierarchy information</summary>
    public Dictionary<Int32, Int32> BoneIdToParentIdMap;
}


public static class OptitrackHiResTimer
{
    public struct Timestamp
    {
        internal Int64 m_ticks;

        public float AgeSeconds
        {
            get
            {
                return Now().SecondsSince( this );
            }
        }

        public float SecondsSince( Timestamp reference )
        {
            Int64 deltaTicks = m_ticks - reference.m_ticks;
            return deltaTicks / (float)System.Diagnostics.Stopwatch.Frequency;
        }
    }

    public static Timestamp Now()
    {
        return new Timestamp {
            m_ticks = System.Diagnostics.Stopwatch.GetTimestamp()
        };
    }
}


/// <summary>
/// Connects to a NatNet streaming server and makes the data available in lightweight Unity-friendly representations.
/// </summary>
public class OptitrackStreamingClient : MonoBehaviour
{
    public enum ClientConnectionType
    {
        Multicast,
        Unicast
    }


    public ClientConnectionType ConnectionType;
    public string LocalAddress = "127.0.0.1";
    public string ServerAddress = "127.0.0.1";
    public OptitrackBoneNameConvention BoneNamingConvention = OptitrackBoneNameConvention.Motive;
    public bool DrawMarkers = false;
    public bool RecordOnPlay = false;

    #region Private fields
    //private UInt16 ServerCommandPort = NatNetConstants.DefaultCommandPort;
    //private UInt16 ServerDataPort = NatNetConstants.DefaultDataPort;

    private bool m_doneSubscriptionNotice = false;
    private bool m_receivedFrameSinceConnect = false;
    private OptitrackHiResTimer.Timestamp m_lastFrameDeliveryTimestamp;
    private Coroutine m_connectionHealthCoroutine = null;

    private NatNetClient m_client;
    private NatNetClient.DataDescriptions m_dataDescs;
    private List<OptitrackRigidBodyDefinition> m_rigidBodyDefinitions = new List<OptitrackRigidBodyDefinition>();
    private List<OptitrackSkeletonDefinition> m_skeletonDefinitions = new List<OptitrackSkeletonDefinition>();

    /// <summary>Maps from a streamed rigid body's ID to its most recent available pose data.</summary>
    private Dictionary<Int32, OptitrackRigidBodyState> m_latestRigidBodyStates = new Dictionary<Int32, OptitrackRigidBodyState>();

    /// <summary>Maps from a streamed skeleton's ID to its most recent available pose data.</summary>
    private Dictionary<Int32, OptitrackSkeletonState> m_latestSkeletonStates = new Dictionary<Int32, OptitrackSkeletonState>();

    /// <summary>Maps from a streamed marker's ID to its most recent available position.</summary>
    private Dictionary<Int32, OptitrackMarkerState> m_latestMarkerStates = new Dictionary<Int32, OptitrackMarkerState>();

    /// <summary>Maps from a streamed rigid body's ID to its component.</summary>
    private Dictionary<Int32, MonoBehaviour> m_rigidBodies = new Dictionary<Int32, MonoBehaviour>();

    /// <summary>Maps from a streamed skeleton names to its component.</summary>
    private Dictionary<string, MonoBehaviour> m_skeletons = new Dictionary<string, MonoBehaviour>();

    /// <summary>Maps from a streamed marker's ID to its sphere game object. Used for drawing markers.</summary>
    private Dictionary<Int32, GameObject> m_latestMarkerSpheres = new Dictionary<Int32, GameObject>();

    /// <summary>
    /// Lock held during access to fields which are potentially modified by <see cref="OnNatNetFrameReceived"/> (which
    /// executes on a separate thread). Note while the lock is held, any frame updates received are simply dropped.
    /// </summary>
    private object m_frameDataUpdateLock = new object();
    #endregion Private fields


    private void Update()
    {
        if (DrawMarkers)
        {
            List<Int32> markerIds = new List<Int32>();
            lock (m_frameDataUpdateLock)
            {
                // Move existing spheres and create new ones if necessary
                foreach (KeyValuePair<Int32, OptitrackMarkerState> markerEntry in m_latestMarkerStates)
                {
                    if (m_latestMarkerSpheres.ContainsKey( markerEntry.Key ))
                    {
                        m_latestMarkerSpheres[markerEntry.Key].transform.position = markerEntry.Value.Position;
                    }
                    else
                    {
                        var sphere = GameObject.CreatePrimitive( PrimitiveType.Sphere );
                        sphere.transform.parent = this.transform;
                        sphere.transform.localScale = new Vector3( markerEntry.Value.Size, markerEntry.Value.Size, markerEntry.Value.Size );
                        sphere.transform.position = markerEntry.Value.Position;
                        m_latestMarkerSpheres[markerEntry.Key] = sphere;
                    }
                    markerIds.Add( markerEntry.Key );
                }
                // find spheres to remove that weren't in the previous frame
                List<Int32> markerSphereIdsToDelete = new List<Int32>();
                foreach (KeyValuePair<Int32, GameObject> markerSphereEntry in m_latestMarkerSpheres)
                {
                    if (!markerIds.Contains( markerSphereEntry.Key ))
                    {
                        // stale marker, tag for removal
                        markerSphereIdsToDelete.Add( markerSphereEntry.Key );
                    }
                }
                // remove stale spheres
                foreach(Int32 markerId in markerSphereIdsToDelete)
                {
                    if(m_latestMarkerSpheres.ContainsKey(markerId))
                    {
                        Destroy( m_latestMarkerSpheres[markerId] );
                        m_latestMarkerSpheres.Remove( markerId );
                    }
                }
            }
        }
        else
        {
            // not drawing markers, remove all marker spheres
            foreach (KeyValuePair<Int32, GameObject> markerSphereEntry in m_latestMarkerSpheres)
            {
                Destroy( m_latestMarkerSpheres[markerSphereEntry.Key] );
            }
            m_latestMarkerSpheres.Clear();
        }
    }

    /// <summary>
    /// Returns the first <see cref="OptitrackStreamingClient"/> component located in the scene.
    /// Provides a convenient, sensible default in the common case where only a single client exists.
    /// Issues a warning if more than one such component is found.
    /// </summary>
    /// <returns>An arbitrary OptitrackClient from the scene, or null if none are found.</returns>
    public static OptitrackStreamingClient FindDefaultClient()
    {
        OptitrackStreamingClient[] allClients = FindObjectsOfType<OptitrackStreamingClient>();

        if ( allClients.Length == 0 )
        {
            Debug.LogError( "Unable to locate any " + typeof( OptitrackStreamingClient ).FullName + " components." );
            return null;
        }
        else if ( allClients.Length > 1 )
        {
            Debug.LogWarning( "Multiple " + typeof( OptitrackStreamingClient ).FullName + " components found in scene; defaulting to first available." );
        }

        return allClients[0];
    }

    /// <summary>
    /// Sends a message to Motive to start recording
    /// </summary>
    /// <returns>A boolean indicating if message was successful.</returns>
    public bool StartRecording()
    {
        if(m_client != null)
        {
            return m_client.RequestCommand("StartRecording");
        }
        return false;
    }

    /// <summary>
    /// Sends a message to Motive to stop recording
    /// </summary>
    /// <returns>A boolean indicating if message was successful.</returns>
    public bool StopRecording()
    {
        if (m_client != null)
        {
            return m_client.RequestCommand("StopRecording");
        }
        return false;
    }


    /// <summary>Get the most recently received state for the specified rigid body.</summary>
    /// <param name="rigidBodyId">Corresponds to the "User ID" field in Motive.</param>
    /// <returns>The most recent available state, or null if none available.</returns>
    public OptitrackRigidBodyState GetLatestRigidBodyState( Int32 rigidBodyId, bool networkCompensation = true)
    {
        OptitrackRigidBodyState rbState;

        if ( ! networkCompensation || m_client == null )
        {
            lock ( m_frameDataUpdateLock )
            {
                m_latestRigidBodyStates.TryGetValue( rigidBodyId, out rbState );
            }
        }
        else
        {
            sRigidBodyData rbData;
            m_client.GetPredictedRigidBodyPose( rigidBodyId, out rbData, 0.0 );

            rbState = new OptitrackRigidBodyState();
            RigidBodyDataToState( rbData, OptitrackHiResTimer.Now(), rbState );
        }

        return rbState;
    }   


    /// <summary>Get the most recently received state for the specified skeleton.</summary>
    /// <param name="skeletonId">
    /// Taken from the corresponding <see cref="OptitrackSkeletonDefinition.Id"/> field.
    /// To find the appropriate skeleton definition, use <see cref="GetSkeletonDefinitionByName"/>.
    /// </param>
    /// <returns>The most recent available state, or null if none available.</returns>
    public OptitrackSkeletonState GetLatestSkeletonState( Int32 skeletonId )
    {
        OptitrackSkeletonState skelState;

        lock ( m_frameDataUpdateLock )
        {
            m_latestSkeletonStates.TryGetValue( skeletonId, out skelState );
        }

        return skelState;
    }

    /// <summary>Get the most recently received state for streamed markers.</summary>
    /// <returns>The most recent available marker states, or null if none available.</returns>
    public List<OptitrackMarkerState> GetLatestMarkerStates()
    {
        List<OptitrackMarkerState> markerStates = new List<OptitrackMarkerState>();

        lock (m_frameDataUpdateLock)
        {
            foreach (KeyValuePair<Int32, OptitrackMarkerState> markerEntry in m_latestMarkerStates)
            {
                OptitrackMarkerState newMarkerState = new OptitrackMarkerState
                {
                    Position = markerEntry.Value.Position,
                    Labeled = markerEntry.Value.Labeled,
                    Size = markerEntry.Value.Size,
                    Id = markerEntry.Value.Id
                };
                markerStates.Add( newMarkerState );
            }
        }

        return markerStates;
    }


    /// <summary>Retrieves the definition of the rigid body with the specified streaming ID.</summary>
    /// <param name="rigidBodyId"></param>
    /// <returns>The specified rigid body definition, or null if not found.</returns>
    public OptitrackRigidBodyDefinition GetRigidBodyDefinitionById( Int32 rigidBodyId )
    {
        for ( int i = 0; i < m_rigidBodyDefinitions.Count; ++i )
        {
            OptitrackRigidBodyDefinition rbDef = m_rigidBodyDefinitions[i];

            if ( rbDef.Id == rigidBodyId )
            {
                return rbDef;
            }
        }

        return null;
    }


    /// <summary>Retrieves the definition of the skeleton with the specified asset name.</summary>
    /// <param name="skeletonAssetName">The name of the skeleton for which to retrieve the definition.</param>
    /// <returns>The specified skeleton definition, or null if not found.</returns>
    public OptitrackSkeletonDefinition GetSkeletonDefinitionByName( string skeletonAssetName )
    {
        for ( int i = 0; i < m_skeletonDefinitions.Count; ++i )
        {
            OptitrackSkeletonDefinition skelDef = m_skeletonDefinitions[i];

            if ( skelDef.Name.Equals( skeletonAssetName, StringComparison.InvariantCultureIgnoreCase ) )
            {
                return skelDef;
            }
        }

        return null;
    }

    /// <summary>Retrieves the definition of the skeleton with the specified skeleton id.</summary>
    /// <param name="skeletonId">The id of the skeleton for which to retrieve the definition.</param>
    /// <returns>The specified skeleton definition, or null if not found.</returns>
    public OptitrackSkeletonDefinition GetSkeletonDefinitionById( Int32 skeletonId )
    {
        for (int i = 0; i < m_skeletonDefinitions.Count; ++i)
        {
            OptitrackSkeletonDefinition skelDef = m_skeletonDefinitions[i];

            if (skelDef.Id == skeletonId)
            {
                return skelDef;
            }
        }

        return null;
    }

    /// <summary>Request data descriptions from the host, then update our definitions.</summary>
    /// <exception cref="NatNetException">
    /// Thrown by <see cref="NatNetClient.GetDataDescriptions"/> if the request to the server fails.
    /// </exception>
    public void UpdateDefinitions()
    {
        // This may throw an exception if the server request times out or otherwise fails.
        UInt32 descriptionTypeMask = 0;
        descriptionTypeMask |= (1 << (int)NatNetDataDescriptionType.NatNetDataDescriptionType_RigidBody);
        descriptionTypeMask |= (1 << (int)NatNetDataDescriptionType.NatNetDataDescriptionType_Skeleton);
        m_dataDescs = m_client.GetDataDescriptions(descriptionTypeMask); //

        m_rigidBodyDefinitions.Clear();
        m_skeletonDefinitions.Clear();

        // Translate rigid body definitions.
        for ( int nativeRbDescIdx = 0; nativeRbDescIdx < m_dataDescs.RigidBodyDescriptions.Count; ++nativeRbDescIdx )
        {
            sRigidBodyDescription nativeRb = m_dataDescs.RigidBodyDescriptions[nativeRbDescIdx];

            OptitrackRigidBodyDefinition rbDef = new OptitrackRigidBodyDefinition {
                Id = nativeRb.Id,
                Name = nativeRb.Name,
                Markers = new List<OptitrackRigidBodyDefinition.MarkerDefinition>( nativeRb.MarkerCount ),
            };

            // Populate nested marker definitions.
            for ( int nativeMarkerIdx = 0; nativeMarkerIdx < nativeRb.MarkerCount; ++nativeMarkerIdx )
            {
                int positionOffset = nativeMarkerIdx * Marshal.SizeOf( typeof( MarkerDataVector ) );
                IntPtr positionPtr = new IntPtr( nativeRb.MarkerPositions.ToInt64() + positionOffset );

                int labelOffset = nativeMarkerIdx * Marshal.SizeOf( typeof( Int32 ) );
                IntPtr labelPtr = new IntPtr( nativeRb.MarkerRequiredLabels.ToInt64() + labelOffset );

                MarkerDataVector nativePos =
                    (MarkerDataVector)Marshal.PtrToStructure( positionPtr, typeof( MarkerDataVector ) );

                Int32 nativeLabel = Marshal.ReadInt32( labelPtr );

                OptitrackRigidBodyDefinition.MarkerDefinition markerDef =
                    new OptitrackRigidBodyDefinition.MarkerDefinition {
                        Position = new Vector3( -nativePos.Values[0], nativePos.Values[1], nativePos.Values[2] ),
                        RequiredLabel = nativeLabel,
                    };

                rbDef.Markers.Add( markerDef );
            }

            m_rigidBodyDefinitions.Add( rbDef );
        }

        // Translate skeleton definitions.
        for ( int nativeSkelDescIdx = 0; nativeSkelDescIdx < m_dataDescs.SkeletonDescriptions.Count; ++nativeSkelDescIdx )
        {
            sSkeletonDescription nativeSkel = m_dataDescs.SkeletonDescriptions[nativeSkelDescIdx];

            OptitrackSkeletonDefinition skelDef = new OptitrackSkeletonDefinition {
                Id = nativeSkel.Id,
                Name = nativeSkel.Name,
                Bones = new List<OptitrackSkeletonDefinition.BoneDefinition>(nativeSkel.RigidBodyCount),
                BoneIdToParentIdMap = new Dictionary<int, int>(),
            };

            // Populate nested bone definitions.
            for ( int nativeBoneIdx = 0; nativeBoneIdx < nativeSkel.RigidBodyCount; ++nativeBoneIdx )
            {
                sRigidBodyDescription nativeBone = nativeSkel.RigidBodies[nativeBoneIdx];

                OptitrackSkeletonDefinition.BoneDefinition boneDef =
                    new OptitrackSkeletonDefinition.BoneDefinition {
                        Id = nativeBone.Id,
                        ParentId = nativeBone.ParentId,
                        Name = nativeBone.Name,
                        Offset = new Vector3( -nativeBone.OffsetX, nativeBone.OffsetY, nativeBone.OffsetZ ),
                    };

                skelDef.Bones.Add( boneDef );
                skelDef.BoneIdToParentIdMap[boneDef.Id] = boneDef.ParentId;
            }

            m_skeletonDefinitions.Add( skelDef );
        }
    }


    public void RegisterRigidBody( MonoBehaviour component, Int32 rigidBodyId )
    {
        if ( m_rigidBodies.ContainsKey( rigidBodyId ) )
        {
#if false
            MonoBehaviour existingRb = m_rigidBodies[rigidBodyId];
            Debug.LogError( GetType().FullName + ": " + rb.GetType().FullName + " has duplicate rigid body ID " + rigidBodyId, component );
            Debug.LogError( GetType().FullName + ": (Existing " + existingRb.GetType().FullName + " was already registered with that ID)", existingRb );
            rb.enabled = false;
#endif
            return;
        }

        m_rigidBodies[rigidBodyId] = component;

        SubscribeRigidBody( component, rigidBodyId );
    }

    public void RegisterSkeleton(MonoBehaviour component, string name)
    {
        if (m_skeletons.ContainsKey(name))
        {
#if false
            MonoBehaviour existingSkel = m_skeletons[rigidBodyId];
            Debug.LogError( "Duplicate skeleton detected, " + GetType().FullName + ": (Existing " + existingRb.GetType().FullName + " was already registered with that ID)", existingRb );
#endif
            return;
        }

        m_skeletons[name] = component;
        SubscribeSkeleton(component, name);
    }


    /// <summary>
    /// (Re)initializes <see cref="m_client"/> and connects to the configured streaming server.
    /// </summary>
    void OnEnable()
    {
        IPAddress serverAddr = IPAddress.Parse( ServerAddress );
        IPAddress localAddr = IPAddress.Parse( LocalAddress );

        NatNetConnectionType connType;
        switch ( ConnectionType )
        {
            case ClientConnectionType.Unicast:
                connType = NatNetConnectionType.NatNetConnectionType_Unicast;
                break;
            case ClientConnectionType.Multicast:
            default:
                connType = NatNetConnectionType.NatNetConnectionType_Multicast;
                break;
        }

        try
        {
            m_client = new NatNetClient();
            m_client.Connect( connType, localAddr, serverAddr );

            UpdateDefinitions();

            // clear all subscriptions
            if (ConnectionType == ClientConnectionType.Unicast)
            {
                ResetStreamingSubscriptions();
            }

            if (ConnectionType == ClientConnectionType.Unicast)
            {
                // Re-subscribe to rigid bodies and/or skeletons in which the streaming client has data
                foreach (KeyValuePair<Int32, MonoBehaviour> rb in m_rigidBodies)
                {
                    SubscribeRigidBody(rb.Value, rb.Key);
                }
                foreach (KeyValuePair<string, MonoBehaviour> skel in m_skeletons)
                {
                    SubscribeSkeleton(skel.Value, skel.Key);
                }
            }

            

            // Set the Skeleton Coordinates to Global.
            m_client.RequestCommand("SetProperty,,Skeleton Coordinates,false");



            if ( RecordOnPlay == true )
            {
                StartRecording();
            }
            
        }
        catch ( Exception ex )
        {
            Debug.LogException( ex, this );
            Debug.LogError( GetType().FullName + ": Error connecting to server; check your configuration, and make sure the server is currently streaming.", this );
            this.enabled = false;
            return;
        }

        m_client.NativeFrameReceived += OnNatNetFrameReceived;
        m_connectionHealthCoroutine = StartCoroutine( CheckConnectionHealth() );
    }


    /// <summary>
    /// Disconnects from the streaming server and cleans up <see cref="m_client"/>.
    /// </summary>
    void OnDisable()
    {
        if ( m_connectionHealthCoroutine != null )
        {
            StopCoroutine( m_connectionHealthCoroutine );
            m_connectionHealthCoroutine = null;
        }

        if (RecordOnPlay == true)
        {
            StopRecording();
        }

        m_client.NativeFrameReceived -= OnNatNetFrameReceived;
        m_client.Disconnect();
        m_client.Dispose();
        m_client = null;
    }


    System.Collections.IEnumerator CheckConnectionHealth()
    {
        const float kHealthCheckIntervalSeconds = 1.0f;
        const float kRecentFrameThresholdSeconds = 5.0f;

        // The lifespan of these variables is tied to the lifespan of a single connection session.
        // The coroutine is stopped on disconnect and restarted on connect.
        YieldInstruction checkIntervalYield = new WaitForSeconds( kHealthCheckIntervalSeconds );
        OptitrackHiResTimer.Timestamp connectionInitiatedTimestamp = OptitrackHiResTimer.Now();
        OptitrackHiResTimer.Timestamp lastFrameReceivedTimestamp;
        bool wasReceivingFrames = false;
        bool warnedPendingFirstFrame = false;

        while ( true )
        {
            yield return checkIntervalYield;

            if ( m_receivedFrameSinceConnect == false )
            {
                // Still waiting for first frame. Warn exactly once if this takes too long.
                if ( connectionInitiatedTimestamp.AgeSeconds > kRecentFrameThresholdSeconds )
                {
                    if ( warnedPendingFirstFrame == false )
                    {
                        Debug.LogWarning( GetType().FullName + ": No frames received from the server yet. Verify your connection settings are correct and that the server is streaming.", this );
                        warnedPendingFirstFrame = true;
                    }

                    continue;
                }
            }
            else
            {
                // We've received at least one frame, do ongoing checks for changes in connection health.
                lastFrameReceivedTimestamp.m_ticks = Interlocked.Read( ref m_lastFrameDeliveryTimestamp.m_ticks );
                bool receivedRecentFrame = lastFrameReceivedTimestamp.AgeSeconds < kRecentFrameThresholdSeconds;

                if ( wasReceivingFrames == false && receivedRecentFrame == true )
                {
                    // Transition: Bad health -> good health.
                    wasReceivingFrames = true;
                    Debug.Log( GetType().FullName + ": Receiving streaming data from the server.", this );
                    continue;
                }
                else if ( wasReceivingFrames == true && receivedRecentFrame == false )
                {
                    // Transition: Good health -> bad health.
                    wasReceivingFrames = false;
                    Debug.LogWarning( GetType().FullName + ": No streaming frames received from the server recently.", this );
                    continue;
                }
            }
        }
    }


#region Private methods
    /// <summary>
    /// Event handler for NatNet frame delivery. Updates our simplified state representations.
    /// NOTE: This executes in the context of the NatNetLib network service thread!
    /// </summary>
    /// <remarks>
    /// Because the <see cref="sFrameOfMocapData"/> type is expensive to marshal, we instead utilize the
    /// <see cref="NatNetClient.NativeFrameReceivedEventArgs.NativeFramePointer"/>, treating it as as opaque, and
    /// passing it to some helper "accessor" functions to retrieve the subset of data we care about, using only
    /// blittable types which do not cause any garbage to be allocated.
    /// </remarks>
    /// <param name="sender"></param>
    /// <param name="eventArgs"></param>
    private void OnNatNetFrameReceived( object sender, NatNetClient.NativeFrameReceivedEventArgs eventArgs )
    {
        // In the event of contention, drop the frame being delivered and return immediately.
        // We don't want to stall NatNetLib's internal network service thread.
        if ( ! Monitor.TryEnter( m_frameDataUpdateLock ) )
        {
            return;
        }

        try
        {
            // Update health markers.
            m_receivedFrameSinceConnect = true;
            Interlocked.Exchange( ref m_lastFrameDeliveryTimestamp.m_ticks, OptitrackHiResTimer.Now().m_ticks );

            // Process received frame.
            IntPtr pFrame = eventArgs.NativeFramePointer;
            NatNetError result = NatNetError.NatNetError_OK;

            // get timestamp
            UInt64 transmitTimestamp;
            result = NaturalPoint.NatNetLib.NativeMethods.NatNet_Frame_GetTransmitTimestamp(pFrame, out transmitTimestamp);

            // get and decode timecode (if available)
            UInt32 timecode;
            UInt32 timecodeSubframe;
            result = NaturalPoint.NatNetLib.NativeMethods.NatNet_Frame_GetTimecode(pFrame, out timecode, out timecodeSubframe);
            Int32 hour, minute, second, frameNumber, subframeNumber;
            NaturalPoint.NatNetLib.NativeMethods.NatNet_DecodeTimecode(timecode, timecodeSubframe, out hour, out minute, out second, out frameNumber, out subframeNumber);

            // Update rigid bodies.
            Int32 frameRbCount;
            result = NaturalPoint.NatNetLib.NativeMethods.NatNet_Frame_GetRigidBodyCount( pFrame, out frameRbCount );
            NatNetException.ThrowIfNotOK( result, "NatNet_Frame_GetRigidBodyCount failed." );

            for (int rbIdx = 0; rbIdx < frameRbCount; ++rbIdx)
            {
                sRigidBodyData rbData = new sRigidBodyData();
                result = NaturalPoint.NatNetLib.NativeMethods.NatNet_Frame_GetRigidBody( pFrame, rbIdx, out rbData );
                NatNetException.ThrowIfNotOK( result, "NatNet_Frame_GetRigidBody failed." );

                bool bTrackedThisFrame = (rbData.Params & 0x01) != 0;
                if (bTrackedThisFrame == false)
                {
                    continue;
                }

                // Ensure we have a state corresponding to this rigid body ID.
                OptitrackRigidBodyState rbState = GetOrCreateRigidBodyState( rbData.Id );
                RigidBodyDataToState(rbData, OptitrackHiResTimer.Now(), rbState);
            }

            // Update skeletons.
            Int32 frameSkeletonCount;
            result = NaturalPoint.NatNetLib.NativeMethods.NatNet_Frame_GetSkeletonCount( pFrame, out frameSkeletonCount );
            NatNetException.ThrowIfNotOK( result, "NatNet_Frame_GetSkeletonCount failed." );

            for (int skelIdx = 0; skelIdx < frameSkeletonCount; ++skelIdx)
            {
                Int32 skeletonId;
                result = NaturalPoint.NatNetLib.NativeMethods.NatNet_Frame_Skeleton_GetId( pFrame, skelIdx, out skeletonId );
                NatNetException.ThrowIfNotOK( result, "NatNet_Frame_Skeleton_GetId failed." );

                // Ensure we have a state corresponding to this skeleton ID.
                OptitrackSkeletonState skelState = GetOrCreateSkeletonState( skeletonId );

                // Enumerate this skeleton's bone rigid bodies.
                Int32 skelRbCount;
                result = NaturalPoint.NatNetLib.NativeMethods.NatNet_Frame_Skeleton_GetRigidBodyCount( pFrame, skelIdx, out skelRbCount );
                NatNetException.ThrowIfNotOK( result, "NatNet_Frame_Skeleton_GetRigidBodyCount failed." );

                for (int boneIdx = 0; boneIdx < skelRbCount; ++boneIdx)
                {
                    sRigidBodyData boneData = new sRigidBodyData();
                    result = NaturalPoint.NatNetLib.NativeMethods.NatNet_Frame_Skeleton_GetRigidBody( pFrame, skelIdx, boneIdx, out boneData );
                    NatNetException.ThrowIfNotOK( result, "NatNet_Frame_Skeleton_GetRigidBody failed." );

                    // In the context of frame data (unlike in the definition data), this ID value is a
                    // packed composite of both the asset/entity (skeleton) ID and member (bone) ID.
                    Int32 boneSkelId, boneId;
                    NaturalPoint.NatNetLib.NativeMethods.NatNet_DecodeID( boneData.Id, out boneSkelId, out boneId );

                    // TODO: Could pre-populate this map when the definitions are retrieved.
                    // Should never allocate after the first frame, at least.
                    if (skelState.BonePoses.ContainsKey( boneId ) == false)
                    {
                        skelState.BonePoses[boneId] = new OptitrackPose();
                    }
                    if (skelState.LocalBonePoses.ContainsKey(boneId) == false)
                    {
                        skelState.LocalBonePoses[boneId] = new OptitrackPose();
                    }

                    // Flip coordinate handedness from right to left by inverting X and W.
                    Vector3 bonePos = new Vector3(-boneData.X, boneData.Y, boneData.Z);
                    Quaternion boneOri = new Quaternion(-boneData.QX, boneData.QY, boneData.QZ, -boneData.QW);
                    skelState.BonePoses[boneId].Position = bonePos;
                    skelState.BonePoses[boneId].Orientation = boneOri;

                    Vector3 parentBonePos = new Vector3(0,0,0);
                    Quaternion parentBoneOri = new Quaternion(0,0,0,1);

                    OptitrackSkeletonDefinition skelDef = GetSkeletonDefinitionById(skeletonId);
                    if (skelDef == null)
                    {
                        Debug.LogError(GetType().FullName + ": OnNatNetFrameReceived, no corresponding skeleton definition for received skeleton frame data.", this);
                        continue;
                    }

                    Int32 pId = skelDef.BoneIdToParentIdMap[boneId];
                    if (pId != 0)
                    {
                        parentBonePos = skelState.BonePoses[pId].Position;
                        parentBoneOri = skelState.BonePoses[pId].Orientation;
                    }
                    skelState.LocalBonePoses[boneId].Position = bonePos - parentBonePos;
                    skelState.LocalBonePoses[boneId].Orientation = Quaternion.Inverse(parentBoneOri) * boneOri;
                }
            }

            // Update markers
            Int32 MarkerCount;
            result = NaturalPoint.NatNetLib.NativeMethods.NatNet_Frame_GetLabeledMarkerCount( pFrame, out MarkerCount );
            NatNetException.ThrowIfNotOK( result, "NatNet_Frame_GetSkeletonCount failed." );

            m_latestMarkerStates.Clear();

            for (int markerIdx = 0; markerIdx < MarkerCount; ++markerIdx)
            {
                sMarker marker = new sMarker();
                result = NaturalPoint.NatNetLib.NativeMethods.NatNet_Frame_GetLabeledMarker( pFrame, markerIdx, out marker );
                NatNetException.ThrowIfNotOK( result, "NatNet_Frame_GetLabeledMarker failed." );

                // Flip coordinate handedness
                OptitrackMarkerState markerState = GetOrCreateMarkerState( marker.Id );
                markerState.Position = new Vector3( -marker.X, marker.Y, marker.Z );
                markerState.Size = marker.Size;
                markerState.Labeled = (marker.Params & 0x10) == 0;
                markerState.Id = marker.Id;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError( GetType().FullName + ": OnNatNetFrameReceived encountered an exception.", this );
            Debug.LogException( ex, this );
        }
        finally
        {
            Monitor.Exit( m_frameDataUpdateLock );
        }
    }

    private void RigidBodyDataToState(sRigidBodyData rbData, OptitrackHiResTimer.Timestamp timestamp, OptitrackRigidBodyState rbState)
    {
        rbState.DeliveryTimestamp = timestamp;
        rbState.Pose = new OptitrackPose
        {
            Position = new Vector3(-rbData.X, rbData.Y, rbData.Z),
            Orientation = new Quaternion(-rbData.QX, rbData.QY, rbData.QZ, -rbData.QW),
        };
    }

    private void ResetStreamingSubscriptions()
    {
        m_client.RequestCommand( "SubscribeToData" ); // Clear all filters
        m_client.RequestCommand( "SubscribeToData,AllTypes,None" ); // Unsubscribe from all data by default
    }


    private void SubscribeRigidBody( MonoBehaviour component, Int32 rigidBodyId )
    {
        if ( m_client != null && ConnectionType == ClientConnectionType.Unicast )
        {
            bool subscribeSucceeded = m_client.RequestCommand( "SubscribeByID,RigidBody," + rigidBodyId );

            // Log a warning on the first failure.
            if ( ! subscribeSucceeded && ! m_doneSubscriptionNotice )
            {
                if ( m_client.ServerDescription.HostApp == "Motive" )
                {
                    // Host app is Motive: If new enough to support subscription, failure is an error.
                    // Otherwise, warn them that they may want to update Motive to reduce bandwidth consumption.
                    if ( m_client.ServerAppVersion >= new Version(2, 2, 0) )
                    {
                        Debug.LogError( "Failed to subscribe to rigid body streaming data for component", component );
                    }
                    else
                    {
                        Debug.LogWarning( "Your version of Motive is too old to support NatNet data subscription; streaming bandwidth consumption may be higher than necessary. Consider updating Motive to version 2.2.0 or greater." );
                    }
                }
                else
                {
                    // Not Motive, we don't know whether it "should" support this. Warning instead of error.
                    Debug.LogWarning( "Failed to subscribe to rigid body streaming data for component", component );
                }

                m_doneSubscriptionNotice = true;
            }
        }
    }

    private void SubscribeSkeleton(MonoBehaviour component, string name)
    {
        if (m_client != null && ConnectionType == ClientConnectionType.Unicast)
        {
            bool subscribeSucceeded = m_client.RequestCommand("SubscribeToData,Skeleton," + name);

            // Log a warning on the first failure.
            if (!subscribeSucceeded && !m_doneSubscriptionNotice)
            {
                if (m_client.ServerDescription.HostApp == "Motive")
                {
                    // Host app is Motive: If new enough to support subscription, failure is an error.
                    // Otherwise, warn them that they may want to update Motive to reduce bandwidth consumption.
                    if (m_client.ServerAppVersion >= new Version(2, 2, 0))
                    {
                        Debug.LogError("Failed to subscribe to skeleton streaming data for component", component);
                    }
                    else
                    {
                        Debug.LogWarning("Your version of Motive is too old to support NatNet data subscription; streaming bandwidth consumption may be higher than necessary. Consider updating Motive to version 2.2.0 or greater.");
                    }
                }
                else
                {
                    // Not Motive, we don't know whether it "should" support this. Warning instead of error.
                    Debug.LogWarning("Failed to subscribe to skeleton streaming data for component", component);
                }

                m_doneSubscriptionNotice = true;
            }
        }
    }


    /// <summary>
    /// Returns the <see cref="OptitrackRigidBodyState"/> corresponding to the provided <paramref name="rigidBodyId"/>.
    /// If the requested state object does not exist yet, it will initialize and return a newly-created one.
    /// </summary>
    /// <remarks>Makes the assumption that the lock on <see cref="m_frameDataUpdateLock"/> is already held.</remarks>
    /// <param name="rigidBodyId">The ID of the rigid body for which to retrieve the corresponding state.</param>
    /// <returns>The existing state object, or a newly created one if necessary.</returns>
    private OptitrackRigidBodyState GetOrCreateRigidBodyState( Int32 rigidBodyId )
    {
        OptitrackRigidBodyState returnedState = null;

        if ( m_latestRigidBodyStates.ContainsKey( rigidBodyId ) )
        {
            returnedState = m_latestRigidBodyStates[rigidBodyId];
        }
        else
        {
            OptitrackRigidBodyState newRbState = new OptitrackRigidBodyState {
                Pose = new OptitrackPose(),
            };

            m_latestRigidBodyStates[rigidBodyId] = newRbState;

            returnedState = newRbState;
        }

        return returnedState;
    }


    /// <summary>
    /// Returns the <see cref="OptitrackSkeletonState"/> corresponding to the provided <paramref name="skeletonId"/>.
    /// If the requested state object does not exist yet, it will initialize and return a newly-created one.
    /// </summary>
    /// <remarks>Makes the assumption that the lock on <see cref="m_frameDataUpdateLock"/> is already held.</remarks>
    /// <param name="skeletonId">The ID of the skeleton for which to retrieve the corresponding state.</param>
    /// <returns>The existing state object, or a newly created one if necessary.</returns>
    private OptitrackSkeletonState GetOrCreateSkeletonState( Int32 skeletonId )
    {
        OptitrackSkeletonState returnedState = null;

        if ( m_latestSkeletonStates.ContainsKey( skeletonId ) )
        {
            returnedState = m_latestSkeletonStates[skeletonId];
        }
        else
        {
            OptitrackSkeletonState newSkeletonState = new OptitrackSkeletonState {
                BonePoses = new Dictionary<Int32, OptitrackPose>(),
                LocalBonePoses = new Dictionary<int, OptitrackPose>(),
            };

            m_latestSkeletonStates[skeletonId] = newSkeletonState;

            returnedState = newSkeletonState;
        }

        return returnedState;
    }


    /// <summary>
    /// Returns the <see cref="OptitrackMarkerState"/> corresponding to the provided <paramref name="markerId"/>.
    /// If the requested state object does not exist yet, it will initialize and return a newly-created one.
    /// </summary>
    /// <remarks>Makes the assumption that the lock on <see cref="m_frameDataUpdateLock"/> is already held.</remarks>
    /// <param name="markerId">The ID of the rigid body for which to retrieve the corresponding state.</param>
    /// <returns>The existing state object, or a newly created one if necessary.</returns>
    private OptitrackMarkerState GetOrCreateMarkerState(Int32 markerId)
    {
        OptitrackMarkerState returnedState = null;

        if (m_latestMarkerStates.ContainsKey( markerId ))
        {
            returnedState = m_latestMarkerStates[markerId];
        }
        else
        {
            OptitrackMarkerState newMarkerState = new OptitrackMarkerState
            {
                Position = new Vector3(),
            };

            m_latestMarkerStates[markerId] = newMarkerState;

            returnedState = newMarkerState;
        }

        return returnedState;
    }


        public void _EnterFrameDataUpdateLock()
    {
        Monitor.Enter( m_frameDataUpdateLock );
    }


    public void _ExitFrameDataUpdateLock()
    {
        Monitor.Exit( m_frameDataUpdateLock );
    }
#endregion Private methods
}
