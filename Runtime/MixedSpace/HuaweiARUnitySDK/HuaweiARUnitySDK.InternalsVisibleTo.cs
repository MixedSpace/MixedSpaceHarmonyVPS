using System.Runtime.CompilerServices;

// HuaweiARUnitySDK used to live in the predefined Assembly-CSharp, so the bundled
// samples under Assets/Examples could touch HuaweiARInternal internals
// (ARSessionManager, NDKSession, the *Adapter types, ...) directly. After moving
// the SDK into its own assembly those internals became invisible to Assembly-CSharp
// (error CS0122). Grant the predefined assemblies access to the internals again
// instead of widening the SDK's public API.
//
// Only Assets/Examples (Assembly-CSharp) needs this; the HarmonyOS glue and the
// shipped Samples~ do not reference these internals.
[assembly: InternalsVisibleTo("Assembly-CSharp")]
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
