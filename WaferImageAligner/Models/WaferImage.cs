using OpenCvSharp;
using System;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Linq;

namespace WaferImageAligner.Models
{
    public class WaferImage
    {
        public event EventHandler<string> LogMessage;

        public Mat OriginalImage { get; private set; }
        public Mat ProcessedImage { get; private set; }

        public WaferImage(string imagePath)
        {
            OriginalImage = Cv2.ImRead(imagePath);
            RaiseLogMessage($"Image loaded: {imagePath}");
        }

        public void ProcessImage()
        {
            Mat gray = new Mat();
            Cv2.CvtColor(OriginalImage, gray, ColorConversionCodes.BGR2GRAY);

            CircleSegment[] circles = Cv2.HoughCircles(gray, HoughModes.Gradient, 1, gray.Rows / 8, 100, 30, 100, 0);

            if (circles.Length > 0)
            {
                CircleSegment largestCircle = circles[0];
                LineSegmentPoint flatLine = FindFlatPart(gray, largestCircle);

                Mat visualizedImage = OriginalImage.Clone();
                Cv2.Circle(visualizedImage, (OpenCvSharp.Point)largestCircle.Center, (int)largestCircle.Radius, new Scalar(0, 255, 0), 2);
                Cv2.Line(visualizedImage, flatLine.P1, flatLine.P2, new Scalar(255, 0, 0), 2);

                double angle = CalculateRotationAngle(flatLine);
                RaiseLogMessage($"Calculated rotation angle: {angle}");

                // 회전 방향 조정
                angle = -angle; // 시계 방향으로 회전

                Mat rotationMatrix = Cv2.GetRotationMatrix2D(largestCircle.Center, angle, 1);
                Mat rotated = new Mat();
                Cv2.WarpAffine(OriginalImage, rotated, rotationMatrix, OriginalImage.Size());

                ProcessedImage = CropImage(rotated, largestCircle);
                Cv2.Resize(ProcessedImage, ProcessedImage, new OpenCvSharp.Size(1000, 1000));

                Cv2.ImWrite("detected_circle_and_flat.png", visualizedImage);
                Cv2.ImWrite("rotated_image.png", rotated);
                Cv2.ImWrite("cropped_image.png", ProcessedImage);
                Cv2.ImWrite("final_processed_image.png", ProcessedImage);

                RaiseLogMessage("Image processing completed");
            }
            else
            {
                RaiseLogMessage("No circles detected in the image.");
            }
        }

        private LineSegmentPoint FindFlatPart(Mat gray, CircleSegment circle)
        {
            Mat edges = new Mat();
            Cv2.Canny(gray, edges, 50, 150);

            Mat mask = new Mat(edges.Size(), MatType.CV_8UC1, Scalar.Black);
            Cv2.Circle(mask, new OpenCvSharp.Point((int)circle.Center.X, (int)circle.Center.Y), (int)circle.Radius, Scalar.White, -1);
            Cv2.BitwiseAnd(edges, mask, edges);

            LineSegmentPoint[] lines = Cv2.HoughLinesP(edges, 1, Math.PI / 180, 50, 50, 10);

            if (lines != null && lines.Length > 0)
            {
                LineSegmentPoint longestLine = lines.OrderByDescending(l =>
                    Math.Pow(l.P2.X - l.P1.X, 2) + Math.Pow(l.P2.Y - l.P1.Y, 2)).First();

                Cv2.Line(gray, longestLine.P1, longestLine.P2, new Scalar(255, 0, 0), 2);
                Cv2.ImWrite("detected_flat_part.png", gray);

                return longestLine;
            }

            return new LineSegmentPoint(
                new OpenCvSharp.Point((int)circle.Center.X, (int)(circle.Center.Y + circle.Radius)),
                new OpenCvSharp.Point((int)circle.Center.X, (int)(circle.Center.Y - circle.Radius))
            );
        }

        private double CalculateRotationAngle(LineSegmentPoint line)
        {
            // 선의 각도 계산 (라디안)
            double angleRad = Math.Atan2(line.P2.Y - line.P1.Y, line.P2.X - line.P1.X);

            // 라디안을 도로 변환
            double angleDeg = angleRad * 180 / Math.PI;

            // 각도를 0 ~ 360 범위로 조정
            angleDeg = (angleDeg + 360) % 360;

            double deviation = 90 - angleDeg;
            double deviation2 = deviation + 90;

            // 플랫한 부분이 아래에 있는 경우 (각도가 45도에서 135도 사이)
            if (angleDeg > 45 && angleDeg <= 135)
            {
                deviation2 = deviation - 90; // 이 경우에는 90도를 빼줍니다
            }

            RaiseLogMessage($"Original angle: {angleDeg}, Adjusted rotation angle: {deviation2}");
            return deviation2;
        }


        private Mat CropImage(Mat image, CircleSegment circle)
        {
            int size = (int)Math.Max(circle.Radius * 2, circle.Radius * 2);
            return new Mat(image, new OpenCvSharp.Rect((int)(circle.Center.X - size / 2), (int)(circle.Center.Y - size / 2), size, size));
        }

        public BitmapSource GetBitmapSource()
        {
            if (ProcessedImage == null)
                return null;

            int width = ProcessedImage.Cols;
            int height = ProcessedImage.Rows;
            long step = ProcessedImage.Step();
            IntPtr scan0 = ProcessedImage.Data;

            PixelFormat format = PixelFormats.Bgr24;

            if (step * height > int.MaxValue)
            {
                throw new InvalidOperationException("Image is too large to convert to BitmapSource");
            }

            int stride = (int)step;
            int bufferSize = (int)(stride * height);

            return BitmapSource.Create(
                width, height, 96, 96, format, null,
                scan0, bufferSize, stride);
        }

        private void RaiseLogMessage(string message)
        {
            LogMessage?.Invoke(this, message);
        }
    }
}