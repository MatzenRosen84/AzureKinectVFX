//=========================
// OptiTrack Unity Plugin 
//=========================

The OptiTrack Unity Plugin allows you to stream real-time rigid body and
skeletal animation into Unity.

For more information, refer to the documentation at the following URL:

    http://wiki.optitrack.com/index.php?title=OptiTrack_Unity_Plugin



HMD tracking in Unity has been moved to the following location in Unity: 

    Edit > Project Settings > XR Plugin Management

To use our OpenVR Driver along with Valve's OpenVR XR Unity plugin to use
HMDs in recent versions of Unity. For more information, refer to the 
documentation at the following URL:

    https://wiki.optitrack.com/index.php?title=Unity:_HMD_Setup



Version History / Changelog
---------------------------

1.3.0 (2021-02-10)
-----
- Unicast Subscription - Added the ability to subscribe to data with Unicast 
  connection to Motive. This ensures that only necessary information is streamed
  to the client minimizing wireless traffic. (Compatible with Motive 2.2+)
- HMDs use XR Plugins - Unity updated their HMD workflow. To use OpenVR with
  Unity you will need the OpenVR XR Plugin from Valve. We have removed the 
  depricated HMD scripts from the plugin. 
- Global Skeleton Coordinates - The plugin now uses global skeleton coordinates
  to standardize between our other plugins. This setting is now automatically 
  changes when you start streaming. 
- Added the ability to start/stop recording in Motive when playing in Unity.
- Added some example code for accessing timecode information when available. 
- Removed depricated query for rigid body marker data.
- Fixed issues with the plugin caused by engine updates. 


1.2.0 (2018-09-19)
-----
- Updated the NatNet DLL which now allows users to stream over 100 rigid bodies. 
- Added Draw Markers option to the OptiTrack Streaming Client object for 
  displaying streamed marker data as sphere objects in the scene.
- Added a rigid body prefab for first-time users.
- Compatibility updates for Unity 2018.2


1.1.0 (2017-12-05)
-----
- Implemented late update functionality when using Unity 2017.1+, which
  reduces render transform latency for both rigid body and HMD components.
- Added support for alternative HMD rigid body streaming orientations.
- Added Mecanim finger retargeting support.
- Improved handling of HMD disconnection and reconnection.
- Compatibility updates for Unity 2017.2.
- Updated NatNetLib to version 3.0.1.


1.0.1 (2016-10-13)
-----
- Improved detection and handling of client connection errors.


1.0.0 (2016-08-23)
-----
- Initial release.
