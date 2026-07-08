using NINA.Core.Utility;
using NINA.Image.FileFormat.FITS;
using NINA.Image.ImageAnalysis;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.Livestack.Image {

    public partial class LiveStackBag {
        public static readonly string NOTARGET = "No_target";
        public static readonly string NOFILTER = "No_filter";
        public static readonly string RED_OSC = "R_OSC";
        public static readonly string GREEN_OSC = "G_OSC";
        public static readonly string BLUE_OSC = "B_OSC";

        public LiveStackBag(string target, string filter, ImageProperties properties, ImageMetaData metaData, List<Accord.Point> referenceStars) {
            Filter = filter;
            Target = target;
            Properties = properties;
            MetaData = metaData;
            ReferenceImageStars = referenceStars;
            ImageCount = 0;
        }

        public ImageProperties Properties { get; private set; }
        public ImageMetaData MetaData { get; }
        public List<Accord.Point> ReferenceImageStars { get; private set; }
        public float[] Stack { get; private set; }

        public string Filter { get; }

        public string Target { get; }
        public int ImageCount { get; private set; }

        public void Add(float[] image) {
            if (Stack == null) {
                Stack = image;
            } else {
                LivestackMediator.GetImageMath().SequentialStack(image, Stack, ImageCount);
            }
            ImageCount++;
        }

        public void AddTransformed(float[] image, double[,] affineMatrix, bool flippedImage) {
            if (Stack == null) {
                Stack = LivestackMediator.GetImageTransformer().ApplyAffineTransformation(image, Properties.Width, Properties.Height, affineMatrix, flippedImage);
            } else {
                LivestackMediator.GetImageTransformer().ApplyAffineTransformationAndStack(image, Stack, ImageCount, Properties.Width, Properties.Height, affineMatrix, flippedImage);
            }
            ImageCount++;
        }

        public void AddTransformed(ushort[] image, double[,] affineMatrix, bool flippedImage) {
            if (Stack == null) {
                Stack = LivestackMediator.GetImageTransformer().ApplyAffineTransformation(image, Properties.Width, Properties.Height, affineMatrix, flippedImage);
            } else {
                LivestackMediator.GetImageTransformer().ApplyAffineTransformationAndStack(image, Stack, ImageCount, Properties.Width, Properties.Height, affineMatrix, flippedImage);
            }
            ImageCount++;
        }

        public void ForcePushReference(ImageProperties properties, List<Accord.Point> referenceStars, float[] stack) {
            Properties = properties;
            ReferenceImageStars = referenceStars;
            Stack = stack;
            ImageCount = 1;
        }

        public void Resume(float[] stack, int imageCount, List<Accord.Point> referenceStars) {
            Stack = stack;
            ImageCount = imageCount;
            ReferenceImageStars = referenceStars;
        }

        private string GetStackFilePath() {
            return GetStackFilePath(Target, Filter);
        }

        private static string GetStackFilePath(string target, string filter) {
            var destinationFolder = Path.Combine(LivestackMediator.Plugin.WorkingDirectory, "stacks");
            if (!Directory.Exists(destinationFolder)) { Directory.CreateDirectory(destinationFolder); }

            var destinationFile = Path.Combine(destinationFolder, CoreUtil.ReplaceAllInvalidFilenameChars($"{target}-{filter}.fits"));
            return destinationFile;
        }

        public static bool TryReadStackFromDisk(string target, string filter, int expectedWidth, int expectedHeight, out float[] stack, out int imageCount) {
            stack = null;
            imageCount = 0;

            var path = GetStackFilePath(target, filter);
            if (!File.Exists(path)) {
                return false;
            }

            try {
                using (var fits = new CFitsioFITSReader(path)) {
                    if (fits.Width != expectedWidth || fits.Height != expectedHeight) {
                        Logger.Warning($"Existing stack for target \"{target}\" filter \"{filter}\" has dimensions {fits.Width}x{fits.Height}, which does not match the incoming frame's {expectedWidth}x{expectedHeight}. Starting a new stack.");
                        return false;
                    }

                    stack = fits.ReadAllPixelsAsFloat();
                    imageCount = (int)fits.ReadLongHeader("IMGCOUNT");
                }

                if (imageCount <= 0) {
                    stack = null;
                    return false;
                }

                return true;
            } catch (Exception ex) {
                Logger.Warning($"Failed to load existing stack for target \"{target}\" filter \"{filter}\" from {path}: {ex.Message}");
                stack = null;
                imageCount = 0;
                return false;
            }
        }

        public static void DeleteStackFile(string target, string filter) {
            var path = GetStackFilePath(target, filter);
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                Logger.Warning($"Failed to delete stack file for target \"{target}\" filter \"{filter}\" at {path}: {ex.Message}");
            }
        }

        public void AutoSaveToDisk() {
            var destinationFile = GetStackFilePath();
            var tempFile = Path.Combine(destinationFile + ".tmp");

            if (File.Exists(tempFile)) {
                File.Delete(tempFile);
            }

            SaveToDisk(tempFile);

            if (File.Exists(destinationFile)) {
                File.Delete(destinationFile);
            }
            File.Move(tempFile, destinationFile);
        }

        public void SaveToDisk(string path) {
            var stackFits = new CFitsioFITSExtendedWriter(path, Stack, Properties.Width, Properties.Height, CfitsioNative.COMPRESSION.NOCOMPRESS);
            stackFits.PopulateHeaderCards(MetaData);
            stackFits.AddHeader("IMGCOUNT", ImageCount, "");
            stackFits.Close();
        }
    }
}
