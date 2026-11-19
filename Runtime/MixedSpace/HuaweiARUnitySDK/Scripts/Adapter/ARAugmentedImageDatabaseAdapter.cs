// Augmented-image database management, on top of the AREngineBridge.
//
// The old layer owned a database handle: it created one on first use, filled it
// through HwArAugmentedImageDatabase_AddImage with an opaque byte blob, and
// reloaded one through _Deserialize. The bridge keeps exactly one database for
// the session and owns its lifetime - TJAREX_CreateImageDatabase replaces it,
// TJAREX_DeserializeImageDatabase destroys the previous one first, and
// TJAREX_DestroySession frees it. So there is no handle to hold here any more:
// every method asks the bridge for its current database, and GetImageDatabase()
// is the only liveness test worth making, because a cached "I created one"
// boolean would go stale the moment the session is rebuilt under this adapter.
//
// One translation is real work rather than plumbing. The old import took a blob
// and its length and let the native side make of it what it would; AREngine's
// ARAugmentedImageSource wants a grayscale pixel array with a width, a height
// and a stride. The bytes reaching SetAugmentedImagePath come from the sample's
// web layer as base64, i.e. an encoded photo, so the decoding happens here now:
// load the picture, turn each pixel into one luma byte.
//
// What this cannot do is make images actually track. The native C API exposes no
// ARConfig_SetAugmentedImageDatabase - the config setters stop at plane finding,
// semantics, mesh and the like - so nothing here attaches the database to the
// session, and GetTrackedImages stays empty. The ArkTS layer is the only place
// that wiring exists. The management path is ported faithfully; whether a
// database built this way ever reports a tracked image is the thing to confirm
// on a real device.

namespace HuaweiARInternal
{
    using System;
    using UnityEngine;
    using AREngineBridge;

    internal class ARAugmentedImageDatabaseAdapter
    {
        public ARAugmentedImageDatabaseAdapter(NDKSession session)
        {
            // The database belongs to the bridge's session, so this adapter needs
            // no handle of its own - but NDKSession builds every adapter the same
            // way, so the signature stays.
        }

        /// <summary>
        ///     adds a photo to the database
        /// </summary>
        /// <param name="buffer">an encoded picture, PNG or JPEG</param>
        /// <param name="name">the name a later AcquireName reads back</param>
        /// <returns>NDKARStatus.ARENGINE_SUCCESS when AREngine accepted it</returns>
        public NDKARStatus SetAugmentedImagePath(byte[] buffer, string name)
        {
            if (!HasDatabase)
            {
                CreateDBHandle();
            }

            int width;
            int height;
            byte[] gray = ToGrayScale(buffer, out width, out height);
            if (gray == null)
            {
                ARDebug.LogError("ARAugmentedImageDatabase.AddImage: {0} bytes is not a PNG or JPEG " +
                    "the bridge can decode; AREngine needs the picture, not a raw buffer.",
                    buffer == null ? 0 : buffer.Length);
                return NDKARStatus.HWAR_ERROR_INVALID_ARGUMENT;
            }

            int index;
            AREngine_ARAddAugmentedImageReason reason;
            // 0 physical width keeps AREngine's own default, which is what the old
            // import sent: it had no way to say how big the print is.
            return ARBridge.ToNDKStatus(AREngineSession.AddImageToDatabase(gray, width, height, width, name, 0f,
                out index, out reason));
        }

        /// <summary>
        ///     replaces the database with one serialised earlier by AREngine
        /// </summary>
        /// <param name="databaseBytes">the bytes GetImageData produced</param>
        /// <returns>NDKARStatus.ARENGINE_SUCCESS when the database was loaded</returns>
        public NDKARStatus CreateAugImgDatabaseFromBytes(byte[] databaseBytes)
        {
            if (databaseBytes == null || databaseBytes.Length == 0)
            {
                ARDebug.LogError("ARAugmentedImageDatabase.Deserialize: nothing to load.");
                return NDKARStatus.HWAR_ERROR_INVALID_ARGUMENT;
            }
            // The bridge destroys whatever database it holds before deserialising,
            // so the old code's throwaway Create-then-overwrite no longer happens.
            return ARBridge.ToNDKStatus(AREngineSession.DeserializeImageDatabase(databaseBytes, databaseBytes.Length));
        }

        /// <summary>
        ///     borrows the serialised database
        /// </summary>
        /// <returns>a pointer to the bytes and how many there are, or Zero and 0
        ///     when there is no database</returns>
        /// <remarks>
        ///     the contract the sample relies on: the memory belongs to AREngine and
        ///     is only good until the database changes, so the caller must copy it
        ///     straight away, which is what AugmentedImageExampleController does
        /// </remarks>
        public (IntPtr, Int64) GetImageData()
        {
            IntPtr outBuffer = IntPtr.Zero;
            Int64 outBufferSize = 0;

            IntPtr database = AREngineExtended.GetImageDatabase();
            if (database == IntPtr.Zero)
            {
                ARDebug.LogError("ARAugmentedImageDatabase.Serialize: there is no database to serialise.");
                return (outBuffer, outBufferSize);
            }

            ulong size = 0;
            if (ARBridge.Ok("ARAugmentedImageDatabase.Serialize",
                    AREngineNative.ARAugmentedImageDatabase_Serialize(database, out outBuffer, out size)))
            {
                outBufferSize = (Int64)size;
            }
            else
            {
                outBuffer = IntPtr.Zero;
            }
            return (outBuffer, outBufferSize);
        }

        public void Destroy(IntPtr dataBaseHandle)
        {
            // Nothing to free: the bridge owns the one database it holds and
            // destroys it with the session. Calling TJARE_ARAugmentedImageDatabase_Destroy
            // on the borrowed pointer would leave the bridge pointing at freed
            // memory, so this stays a no-op even though the old import existed.
        }

        public void CreateDBHandle()
        {
            ARBridge.Ok("ARAugmentedImageDatabase.Create", AREngineSession.CreateImageDatabase());
        }

        public int GetNumImages()
        {
            return AREngineSession.ImageDatabaseCount;
        }

        /// <summary>
        ///     whether the bridge currently holds a database
        /// </summary>
        private static bool HasDatabase
        {
            get { return AREngineExtended.GetImageDatabase() != IntPtr.Zero; }
        }

        /// <summary>
        ///     turns an encoded picture into the grayscale array AREngine wants
        /// </summary>
        /// <param name="buffer">the PNG or JPEG bytes</param>
        /// <param name="width">the picture's width in pixels</param>
        /// <param name="height">the picture's height in pixels</param>
        /// <returns>one byte per pixel, row-major from the top row, or null when the
        ///     bytes are not a decodable picture</returns>
        private static byte[] ToGrayScale(byte[] buffer, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (buffer == null || buffer.Length == 0)
            {
                return null;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, buffer))
                {
                    return null;
                }
                width = texture.width;
                height = texture.height;
                if (width <= 0 || height <= 0)
                {
                    return null;
                }

                Color32[] pixels = texture.GetPixels32();
                byte[] gray = new byte[width * height];
                for (int y = 0; y < height; y++)
                {
                    // GetPixels32 starts at the bottom row and AREngine reads its
                    // array from the top, so the rows go in reverse order - a
                    // vertically mirrored print does not match the real one.
                    int source = (height - 1 - y) * width;
                    int target = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        Color32 c = pixels[source + x];
                        gray[target + x] = (byte)((c.r * 299 + c.g * 587 + c.b * 114) / 1000);
                    }
                }
                return gray;
            }
            catch (Exception e)
            {
                ARDebug.LogError("ARAugmentedImageDatabase: decoding the picture failed: {0}", e.Message);
                width = 0;
                height = 0;
                return null;
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
