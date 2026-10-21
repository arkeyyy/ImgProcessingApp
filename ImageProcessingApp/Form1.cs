using AForge.Video;
using AForge.Video.DirectShow;

namespace ImageProcessingApp
{
    public partial class Form1 : Form
    {
        private FilterInfoCollection videoDevices;
        private VideoCaptureDevice videoSource;

        private enum WebcamProcessMode { None, Grayscale, Invert, Sepia, Subtract }
        private WebcamProcessMode _webcamMode = WebcamProcessMode.None;

        Bitmap originalImage;
        Bitmap processedImage;
        Bitmap imageB, imageA, colorgreen; // for Form 2

        int threshold = 5; // ako gi global para ma access sa subtraction process ug sa webcam subtraction

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

            comboBoxCameras.Items.Clear();
            if (videoDevices.Count == 0)
            {
                comboBoxCameras.Items.Add("No camera found");
                comboBoxCameras.SelectedIndex = 0;
                return;
            }

            // Add each camera's name to the ComboBox
            foreach (FilterInfo device in videoDevices)
            {
                comboBoxCameras.Items.Add(device.Name);
            }

            // Select the first camera by default
            comboBoxCameras.SelectedIndex = 0;
        }

        private void pictureBox1_Click(object sender, EventArgs e) // input image
        {
        }

        private void pictureBox2_Click(object sender, EventArgs e) // output image
        {
        }

        private void button6_Click(object sender, EventArgs e) // Load Image
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Image Files|*.jpg;*.png;*.bmp";
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                originalImage = new Bitmap(ofd.FileName);
                processedImage = new Bitmap(originalImage);
                pictureBox1.Image = originalImage;
                pictureBox2.Image = null;
            }
        }

        private void button7_Click(object sender, EventArgs e) // Copy Image
        {
            if (originalImage == null) return;
            processedImage = new Bitmap(originalImage.Width, originalImage.Height);
            for (int y = 0; y < originalImage.Height; y++)
            {
                for (int x = 0; x < originalImage.Width; x++)
                {
                    Color pixelColor = originalImage.GetPixel(x, y);
                    processedImage.SetPixel(x, y, pixelColor);
                }
            }
            pictureBox2.Image = processedImage;
        }

        private void button1_Click(object sender, EventArgs e) // Grayscale (simple average)
        {
            if (originalImage == null) return;
            Bitmap gray = new Bitmap(originalImage.Width, originalImage.Height);
            for (int y = 0; y < originalImage.Height; y++)
            {
                for (int x = 0; x < originalImage.Width; x++)
                {
                    Color c = originalImage.GetPixel(x, y);
                    int avg = (c.R + c.G + c.B) / 3;   // simple average
                    gray.SetPixel(x, y, Color.FromArgb(avg, avg, avg));
                }
            }

            processedImage = gray;
            pictureBox2.Image = processedImage;
        }


        private void button2_Click(object sender, EventArgs e) // Invert Colors
        {
            if (originalImage == null) return;
            Bitmap inverted = new Bitmap(originalImage.Width, originalImage.Height);
            for (int y = 0; y < originalImage.Height; y++)
            {
                for (int x = 0; x < originalImage.Width; x++)
                {
                    Color c = originalImage.GetPixel(x, y);
                    inverted.SetPixel(x, y, Color.FromArgb(255 - c.R, 255 - c.G, 255 - c.B));
                }
            }
            processedImage = inverted;
            pictureBox2.Image = processedImage;
        }

        private void button3_Click(object sender, EventArgs e) // Sepia Filter
        {
            if (originalImage == null) return;
            Bitmap sepia = new Bitmap(originalImage.Width, originalImage.Height);
            for (int y = 0; y < originalImage.Height; y++)
            {
                for (int x = 0; x < originalImage.Width; x++)
                {
                    Color c = originalImage.GetPixel(x, y);
                    int tr = (int)(0.393 * c.R + 0.769 * c.G + 0.189 * c.B);
                    int tg = (int)(0.349 * c.R + 0.686 * c.G + 0.168 * c.B);
                    int tb = (int)(0.272 * c.R + 0.534 * c.G + 0.131 * c.B);
                    tr = Math.Min(255, tr);
                    tg = Math.Min(255, tg);
                    tb = Math.Min(255, tb);
                    sepia.SetPixel(x, y, Color.FromArgb(tr, tg, tb));
                }
            }
            processedImage = sepia;
            pictureBox2.Image = processedImage;
        }

        private void button4_Click(object sender, EventArgs e) // Histogram
        {
            if (processedImage == null) return;

            int[] histogram = new int[256];
            for (int y = 0; y < processedImage.Height; y++)
            {
                for (int x = 0; x < processedImage.Width; x++)
                {
                    Color c = processedImage.GetPixel(x, y);
                    int gray = (c.R + c.G + c.B) / 3;
                    histogram[gray]++;
                }
            }

            int max = 0;
            foreach (int val in histogram)
                if (val > max) max = val;

            int width = pictureBox2.ClientSize.Width;
            int height = pictureBox2.ClientSize.Height;
            Bitmap histBmp = new Bitmap(width, height);

            using (Graphics g = Graphics.FromImage(histBmp))
            {
                g.Clear(pictureBox2.BackColor);

                float barWidth = (float)width / 256f;

                for (int i = 0; i < 256; i++)
                {
                    float barHeight = (histogram[i] / (float)max) * (height - 1);

                    g.FillRectangle(Brushes.Black,
                        i * barWidth,
                        height - barHeight,
                        barWidth,
                        barHeight);
                }
            }

            pictureBox2.Image = histBmp;
        }

        private void button8_Click(object sender, EventArgs e) // Save button
        {
            if (processedImage == null) return;
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "PNG|*.png|JPEG|*.jpg|Bitmap|*.bmp";
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                processedImage.Save(sfd.FileName);
                MessageBox.Show("Image saved successfully!");
            }
        }
        private void clearProcessed_1_Click(object sender, EventArgs e)
        {
            if (pictureBox2.Image != null) { pictureBox2.Image.Dispose(); pictureBox2.Image = null; }
            if (pictureBox1.Image != null) { pictureBox1.Image.Dispose(); pictureBox1.Image = null; }
        }

        /////////////////////////////// Subtraction Form ///////////////////////////////


        private void button12_Click(object sender, EventArgs e) // Load image (Form 2)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Image Files|*.jpg;*.png;*.bmp";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    imageB = new Bitmap(ofd.FileName);
                    pictureBox5.Image = imageB;
                }
            }
        }

        private void button11_Click(object sender, EventArgs e) // Load Background (Form 2)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Image Files|*.jpg;*.png;*.bmp";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    imageA = new Bitmap(ofd.FileName);
                    pictureBox4.Image = imageA;
                }
            }
        }

        private void button10_Click(object sender, EventArgs e) // Subtract button (Form 2)
        {
            if (imageA == null || imageB == null)
            {
                MessageBox.Show("Please load both images first.");
                return;
            }

            int width = Math.Min(imageA.Width, imageB.Width);
            int height = Math.Min(imageA.Height, imageB.Height);

            Bitmap resultImage = new Bitmap(width, height);

            Color mygreen = Color.FromArgb(0, 255, 0);
            int greygreen = (mygreen.R + mygreen.G + mygreen.B) / 3;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Color pixel = imageB.GetPixel(x, y);
                    Color backpixel = imageA.GetPixel(x, y);

                    int grey = (pixel.R + pixel.G + pixel.B) / 3;

                    int subtractValue = Math.Abs(grey - greygreen);

                    if (subtractValue > threshold)
                        resultImage.SetPixel(x, y, pixel);
                    else
                        resultImage.SetPixel(x, y, backpixel);
                }
            }

            colorgreen = resultImage;
            pictureBox3.Image = colorgreen;
        }

        private void button5_Click(object sender, EventArgs e) // clear Images (Form 2)
        {
            if (pictureBox5.Image != null) { pictureBox5.Image.Dispose(); pictureBox5.Image = null; }
            if (pictureBox4.Image != null) { pictureBox4.Image.Dispose(); pictureBox4.Image = null; }
            if (pictureBox3.Image != null) { pictureBox3.Image.Dispose(); pictureBox3.Image = null; }

            imageA = null;
            imageB = null;
            colorgreen = null;
        }

        private void button9_Click(object sender, EventArgs e) // Save Image (Form 2)
        {
            if (colorgreen == null)
            {
                MessageBox.Show("No result to save.");
                return;
            }

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "PNG|*.png|JPEG|*.jpg|Bitmap|*.bmp";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    colorgreen.Save(sfd.FileName);
                    MessageBox.Show("Subtracted image saved successfully!");
                }
            }
        }


        /////////////////////////////// WebCam Form ///////////////////////////////


        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        protected override void OnFormClosing(FormClosingEventArgs e) // para rani ma close ang camera if gi close ang form
        {
            try
            {
                if (videoSource != null && videoSource.IsRunning)
                {
                    videoSource.SignalToStop();
                    videoSource.WaitForStop();
                    videoSource.NewFrame -= Video_NewFrame;
                    videoSource = null;
                }
            }
            catch
            { }
            base.OnFormClosing(e);  // always call base method last
        }

        private Bitmap ApplyEffect(Bitmap src)
        {
            Bitmap bmp = new Bitmap(src.Width, src.Height);

            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    Color c = src.GetPixel(x, y);

                    switch (_webcamMode)
                    {
                        case WebcamProcessMode.Grayscale:
                            int avg = (c.R + c.G + c.B) / 3;
                            bmp.SetPixel(x, y, Color.FromArgb(avg, avg, avg));
                            break;

                        case WebcamProcessMode.Invert:
                            bmp.SetPixel(x, y, Color.FromArgb(255 - c.R, 255 - c.G, 255 - c.B));
                            break;

                        case WebcamProcessMode.Sepia:
                            int tr = (int)(0.393 * c.R + 0.769 * c.G + 0.189 * c.B);
                            int tg = (int)(0.349 * c.R + 0.686 * c.G + 0.168 * c.B);
                            int tb = (int)(0.272 * c.R + 0.534 * c.G + 0.131 * c.B);
                            tr = Math.Min(255, tr);
                            tg = Math.Min(255, tg);
                            tb = Math.Min(255, tb);
                            bmp.SetPixel(x, y, Color.FromArgb(tr, tg, tb));
                            break;

                        case WebcamProcessMode.Subtract: // same ra sa subtraction process sa Form 2
                            int gray = (c.R + c.G + c.B) / 3;

                            Color mygreen = Color.FromArgb(0, 255, 0);
                            int greygreen = (mygreen.R + mygreen.G + mygreen.B) / 3;

                            int subtractValue = Math.Abs(gray - greygreen);
                            if (subtractValue > threshold)
                                bmp.SetPixel(x, y, c);       // keep foreground
                            else
                                bmp.SetPixel(x, y, Color.Black); // remove green background
                            break;

                        default:
                            bmp.SetPixel(x, y, c);
                            break;
                    }
                }
            }

            return bmp;
        }

        private void Video_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            Bitmap frame = (Bitmap)eventArgs.Frame.Clone();

            // cam input
            pictureBox7.Image?.Dispose();
            pictureBox7.Image = (Bitmap)frame.Clone();

            // processed output sa right picture box
            pictureBox6.Image?.Dispose();
            pictureBox6.Image = ApplyEffect(frame);
            frame.Dispose();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox1.Checked)
            {
                // turn camera ON
                if (videoDevices == null || videoDevices.Count == 0) return;

                int selectedIndex = comboBoxCameras.SelectedIndex;
                if (selectedIndex < 0) return;

                videoSource = new VideoCaptureDevice(videoDevices[selectedIndex].MonikerString);
                videoSource.NewFrame += Video_NewFrame;
                videoSource.Start();
            }
            else
            {
                // turn camera OFF
                if (videoSource != null && videoSource.IsRunning)
                {
                    videoSource.SignalToStop();
                    videoSource.WaitForStop();
                    videoSource.NewFrame -= Video_NewFrame;
                    videoSource = null;
                }

                // clear the picture boxes when camera stops aron chuy
                pictureBox7.Image?.Dispose();
                pictureBox7.Image = null;
                pictureBox6.Image?.Dispose();
                pictureBox6.Image = null;
            }
        }

        private void button15_Click(object sender, EventArgs e) // Grayscale button
        {
            _webcamMode = WebcamProcessMode.Grayscale;
        }

        private void button14_Click(object sender, EventArgs e) // Invert button
        {
            _webcamMode = WebcamProcessMode.Invert;
        }

        private void button13_Click(object sender, EventArgs e) // Sepia Button
        {
            _webcamMode = WebcamProcessMode.Sepia;
        }

        private void button16_Click(object sender, EventArgs e) // subtract button
        {
            _webcamMode = WebcamProcessMode.Subtract;
        }

        private void button17_Click(object sender, EventArgs e) // clear effects button
        {
            _webcamMode = default;
        }

        private void button20_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Image Files|*.jpg;*.png;*.bmp";
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                originalImage = new Bitmap(ofd.FileName);
                processedImage = new Bitmap(originalImage);
                pictureBox9.Image = originalImage;
                pictureBox8.Image = null;
            }
        }

        



        /////////////////////////////// Convolution Matrix Form ///////////////////////////////


        private void Smooth_Click(object sender, EventArgs e)
        {
            if (processedImage == null) return;
            BitmapFilter.Smooth(processedImage);
            pictureBox8.Image = processedImage;
        }

        private void GaussianBlur_Click(object sender, EventArgs e)
        {
            if (processedImage == null) return;

            // for (int i = 0; i < 100; i++) // test lang kay mild ra kayng blur uie
            // joke maka spam raman diay ka sa button
            BitmapFilter.GaussianBlur(processedImage);
            pictureBox8.Image = processedImage;
        }

        private void Sharpen_Click(object sender, EventArgs e)
        {
            if (processedImage == null) return;
            BitmapFilter.Sharpen(processedImage);
            pictureBox8.Image = processedImage;
        }

        private void MeanRemoval_Click(object sender, EventArgs e)
        {
            if (processedImage == null) return;
            BitmapFilter.MeanRemoval(processedImage);
            pictureBox8.Image = processedImage;
        }

        private void Emboss_Click(object sender, EventArgs e)
        {
            if (processedImage == null) return;
            BitmapFilter.Emboss(processedImage);
            pictureBox8.Image = processedImage;
        }

        private void EmbossHorizontalVertical_Click(object sender, EventArgs e)
        {
            if (processedImage == null) return;
            BitmapFilter.EmbossHorizontalVertical(processedImage);
            pictureBox8.Image = processedImage;
        }

        private void EmbossAllDirections_Click(object sender, EventArgs e)
        {
            if (processedImage == null) return;
            BitmapFilter.EmbossAllDirections(processedImage);
            pictureBox8.Image = processedImage;
        }

        private void EmbossLossy_Click(object sender, EventArgs e)
        {
            if (processedImage == null) return;
            BitmapFilter.EmbossLossy(processedImage);
            pictureBox8.Image = processedImage;
        }

        private void EmbossHorizontalOnly_Click(object sender, EventArgs e)
        {
            if (processedImage == null) return;
            BitmapFilter.EmbossHorizontalOnly(processedImage);
            pictureBox8.Image = processedImage;
        }

        private void EmbossVerticalOnly_Click(object sender, EventArgs e)
        {
            if (processedImage == null) return;
            BitmapFilter.EmbossVerticalOnly(processedImage);
            pictureBox8.Image = processedImage;
        }

        private void clearProcessed_Click(object sender, EventArgs e)
        {
            if (pictureBox8.Image != null) { pictureBox8.Image.Dispose(); pictureBox8.Image = null; }
            if (pictureBox9.Image != null) { pictureBox9.Image.Dispose(); pictureBox9.Image = null; }
        }

        private void button18_Click(object sender, EventArgs e)
        {
            if (processedImage == null) return;
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "PNG|*.png|JPEG|*.jpg|Bitmap|*.bmp";
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                processedImage.Save(sfd.FileName);
                MessageBox.Show("Image saved successfully!");
            }
        }


    }
}
