using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

using Emgu.CV;
using Emgu.Util;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;

using System.Net;
//using Rug.Osc;

using VVVV_OSC;

using Leap;

namespace Tracker
{
    public partial class Form1 : Form, ILeapEventDelegate
    {
        private Capture cap;
        private CascadeClassifier classifier;

        private IPAddress address = IPAddress.Parse("127.0.0.1");
        private int port = 8400;

        private int WebcamWidth = 0;
        private int WebcamHeight = 0;
        private System.Windows.Forms.Screen SelectedScreen;
        private int Sesnsitivity = 1;

        private double relativeX = 0;
        private double relativeY = 0;

        private int virtualMouseX = 0;
        private int virtualMouseY = 0;

        private string haarcascadeFolder = "";

        private OSCTransmitter transmitter;

        private string OscXPath = "/1/fader1";
        private decimal OscXMin = 0;
        private decimal OscXMax = 1;
        private string OscYPath = "/1/fader2";
        private decimal OscYMin = 0;
        private decimal OscYMax = 1;

        //private OscSender osc;

        public Form1()
        {
            InitializeComponent();
            haarcascadeFolder = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + @"\haarcascades\";
            try
            {
                //set screens
                Dictionary<int, string> screens = new Dictionary<int, string>();
                var screenIndex = 0;
                var selectedIndex = 0;
                foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                {
                    screens.Add(screenIndex, "Screen " + screenIndex.ToString());
                    if (screen.Primary)
                    {
                        selectedIndex = screenIndex;
                        SelectedScreen = screen;
                    }
                    screenIndex += 1;
                }
                lstScreens.DataSource = new BindingSource(screens, null);
                lstScreens.DisplayMember = "Value";
                lstScreens.ValueMember = "Key";
                lstScreens.SelectedIndex = selectedIndex;

                //set cascades
                Dictionary<string, string> cascades = new Dictionary<string, string>();
                DirectoryInfo di = new DirectoryInfo(haarcascadeFolder);
                int index = 0;
                int selectedCascadeIndex = 0;
                foreach (var cascade in di.GetFiles())
                {
                    cascades.Add(cascade.FullName, cascade.Name);
                    if(cascade.Name == "haarcascade_frontalface_alt2.xml")
                    {
                        selectedCascadeIndex = index;
                    }
                    index += 1;
                }
                lstHaarcascade.DataSource = new BindingSource(cascades, null);
                lstHaarcascade.DisplayMember = "Value";
                lstHaarcascade.ValueMember = "Key";
                lstHaarcascade.SelectedIndex = selectedCascadeIndex;
                
                // passing 0 gets zeroth webcam
                cap = new Capture(0);

                SetHaarcascade();

                //set the OSC values
                txtOscXPath.Text = OscXPath;
                numOscXMin.Value = OscXMin;
                numOscXMax.Value = OscXMax;
                txtOscYPath.Text = OscYPath;
                numOscYMin.Value = OscYMin;
                numOscYMax.Value = OscYMax;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            //LEAP
            this.controller = new Controller();
            this.listener = new LeapEventListener(this);
            controller.AddListener(listener);
        }

        private float detectedXPos = 0f;
        private float detectedYPos = 0f;

        private int histogramCounter = 100;

        private void timer1_Tick(object sender, EventArgs e)
        {
            SendLeapData();
            SendWebCamData();
        }

        private void SendWebCamData()
        {
            //check web cam available
            try
            {
                if (cap == null || classifier == null) return;
                using (Image<Bgr, byte> nextFrame = cap.QueryFrame().ToImage<Bgr, byte>().Flip(FlipType.Horizontal))
                {
                    if (nextFrame != null)
                    {
                        //set the web cam width and height
                        WebcamWidth = nextFrame.Width;
                        WebcamHeight = nextFrame.Height;
                        txtWebcamWidth.Text = WebcamWidth.ToString();
                        txtWebcamHeight.Text = WebcamHeight.ToString();
                        //draw the selected screen
                        double percent = (double)numSensitivity.Value / 100;
                        double screenWidth = WebcamWidth * percent;
                        double screenHeight = ((double)SelectedScreen.Bounds.Height / (double)SelectedScreen.Bounds.Width) * screenWidth;
                        double screenX = ((WebcamWidth - screenWidth) / 2);
                        double screenY = ((WebcamHeight - screenHeight) / 2);
                        txtScreenWidth.Text = screenWidth.ToString();
                        txtScreenHeight.Text = screenHeight.ToString();
                        txtScreenX.Text = screenX.ToString();
                        txtScreenY.Text = screenY.ToString();
                        Rectangle screenRect = new Rectangle((int)screenX, (int)screenY, (int)screenWidth, (int)screenHeight);
                        nextFrame.Draw(screenRect, new Bgr(Color.Blue), 4);

                        //must greyscale image
                        Image<Gray, byte> grayframe = nextFrame.Convert<Gray, byte>();
                        double scaleFactor = double.Parse(txtScaleFactor.Text);
                        int minNeightbours = (int)numMinNeighbours.Value;
                        int minSize = (int)numMinSize.Value;
                        int maxSize = (int)numMaxSize.Value;
                        //do detection and get rectangles
                        Rectangle[] rectangles = classifier.DetectMultiScale(grayframe, scaleFactor, minNeightbours, new Size(minSize, minSize), new Size(maxSize, maxSize));
                        //draw detected rectangles
                        if (rectangles.Count() > 0)
                        {
                            float averageX = (float)rectangles.Select(r => (r.X + (r.Width / 2))).Average();
                            float averageY = (float)rectangles.Select(r => (r.Y + (r.Height / 2))).Average();
                            if (!chkAverageDetection.Checked)
                            {
                                averageX = rectangles[0].X + (rectangles[0].Width / 2);
                                averageY = rectangles[0].Y + (rectangles[0].Height / 2);
                            }
                            detectedXPos = averageX;
                            detectedYPos = averageY;
                            //txtOscXOutput.Text = averageX.ToString();
                            //txtOscYOutput.Text = averageY.ToString();
                            //draw cross for central possition
                            nextFrame.Draw(new Cross2DF(new PointF(averageX, averageY), 20, 20), new Bgr(0, double.MaxValue, 0), 2);

                            if (transmitter != null)
                            {
                                //SEND OSC DATA
                                //from 0 - 127
                                //double val = (127 / (double)(nextFrame.Width)) * averageX;
                                double xposPercentage = (100 / (double)(nextFrame.Width)) * averageX;
                                double yposPercentage = (100 / (double)(nextFrame.Height)) * averageY;
                                if (chkOscXSend.Checked)
                                {
                                    try
                                    {
                                        decimal val = ((OscXMax - OscXMin) / 100) * (decimal)xposPercentage;
                                        txtOscXOutput.Text = val.ToString();
                                        OSCMessage OscXMessage = new OSCMessage(OscXPath, (float)val);
                                        transmitter.Send(OscXMessage);
                                    }
                                    catch (Exception ex)
                                    {
                                        string blah = ex.Message;
                                    }
                                }
                                if (chkOscYSend.Checked)
                                {
                                    try
                                    {
                                        decimal val = ((OscYMax - OscYMin) / 100) * (decimal)yposPercentage;
                                        txtOscYOutput.Text = val.ToString();
                                        OSCMessage OscYMessage = new OSCMessage(OscYPath, (float)val);
                                        transmitter.Send(OscYMessage);
                                    }
                                    catch (Exception ex)
                                    {
                                        string blah = ex.Message;
                                    }
                                }
                                

                                //try
                                //{
                                //    decimal xval = ((OscXMax - OscXMin) / 100) * (decimal)xposPercentage;
                                //    OSCMessage OscXMessage = new OSCMessage(OscXPath, (float)xval);
                                //    transmitter.Send(OscXMessage);

                                //    //OSCMessage message = new OSCMessage("/xpospercentage", val.ToString());
                                //    OSCMessage message = new OSCMessage("/1/fader1", (float)val);
                                //    OSCMessage message2 = new OSCMessage("/1/fader2", (float)xposPercentage);
                                //    OSCMessage message3 = new OSCMessage("/1/fader3", (float)yposPercentage);
                                //    transmitter.Send(message);
                                //    transmitter.Send(message2);
                                //    transmitter.Send(message3);

                                //    //if(osc != null)
                                //    //{
                                //    //    osc.Send(new OscMessage("/xpospercentage", val.ToString()));
                                //    //    //osc.Send(new OscMessage("/ypospercentage", yposPercentage));
                                //    //    //osc.Send(new OscMessage("/playbackspeed", val));
                                //    //}
                                //}
                                //catch (Exception ex)
                                //{
                                //    string blah = ex.Message;
                                //}

                                //using (OscSender osc = new OscSender(address, port))
                                //{
                                //    try
                                //    {
                                //        osc.Connect();
                                //        osc.Send(new OscMessage("/xpospercentage", val.ToString()));
                                //        //osc.Send(new OscMessage("/ypospercentage", yposPercentage));
                                //        //osc.Send(new OscMessage("/playbackspeed", val));
                                //    }
                                //    catch (Exception ex)
                                //    {
                                //        string blah = ex.Message;
                                //    }
                                //    textBox3.Text = val.ToString();
                                //}
                                //SEND OSC DATA
                            }
                        }

                        //work out relative x and y to the screen rect
                        relativeX = detectedXPos - screenX;
                        relativeY = detectedYPos - screenY;
                        //keep the relative within bounds
                        relativeX = (relativeX < 0) ? 0 : (relativeX > screenWidth) ? screenWidth : relativeX;
                        relativeY = (relativeY < 0) ? 0 : (relativeY > screenHeight) ? screenHeight : relativeY;
                        txtRelativeX.Text = relativeX.ToString();
                        txtRelativeY.Text = relativeY.ToString();

                        if (chkDetectionBoxes.Checked)
                        {
                            if (!chkAverageDetection.Checked)
                            {
                                nextFrame.Draw(rectangles[0], new Bgr(0, double.MaxValue, 0), 1);
                            }
                            else
                            {
                                //add a rectangle for each
                                foreach (var face in rectangles)
                                {
                                    nextFrame.Draw(face, new Bgr(0, double.MaxValue, 0), 1);
                                }
                            }
                        }

                        //set aspect ration
                        var boundWidth = SelectedScreen.Bounds.Width;
                        var boundHeight = SelectedScreen.Bounds.Height;
                        var gdc = Gdc(boundWidth, boundHeight);
                        lblRatio.Text = boundWidth.ToString() + "x" + boundHeight.ToString() + " (" + (boundWidth / gdc).ToString() + ":" + (boundHeight / gdc).ToString() + ")";

                        //show mouse position
                        txtMouseX.Text = VirtualMouse.MousePositionX().ToString();
                        txtMouseY.Text = VirtualMouse.MousePositionY().ToString();

                        //create virtual mouse position
                        virtualMouseX = (int)((SelectedScreen.Bounds.Width / screenWidth) * relativeX);
                        virtualMouseY = (int)((SelectedScreen.Bounds.Height / screenHeight) * relativeY);
                        txtVirtualMouseX.Text = virtualMouseX.ToString();
                        txtVirtualMouseY.Text = virtualMouseY.ToString();

                        float min = 0;
                        float max = 65535;

                        txtVirtualMouseX.Text = VirtualMouse.Remap(virtualMouseX, 0.0f, (float)screenWidth, min, max).ToString();
                        txtVirtualMouseY.Text = VirtualMouse.Remap(virtualMouseY, 0.0f, (float)screenHeight, min, max).ToString();

                        //move the mouse if checked
                        if (chkMouse.Checked)
                        {
                            this.Cursor = new Cursor(Cursor.Current.Handle);
                            Cursor.Position = new Point(virtualMouseX, virtualMouseY);
                            Cursor.Current = Cursors.Arrow;


                            //VirtualMouse.MoveTo(virtualMouseX, virtualMouseY, (float)screenWidth, (float)screenHeight);
                        }

                        //do some color detection
                        //convert the image to hsv
                        Image<Hsv, Byte> hsvimg = nextFrame.Convert<Hsv, Byte>();

                        //extract the hue and value channels
                        Image<Gray, Byte>[] channels = hsvimg.Split();  //split into components
                        Image<Gray, Byte> imghue = channels[0];            //hsv, so channels[0] is hue.
                        Image<Gray, Byte> imgval = channels[2];            //hsv, so channels[2] is value.

                        //filter out all but "the color you want"...seems to be 0 to 128 ?
                        Image<Gray, byte> huefilter = imghue.InRange(new Gray(23), new Gray(27));

                        //use the value channel to filter out all but brighter colors
                        Image<Gray, byte> valfilter = imgval.InRange(new Gray(150), new Gray(255));

                        //now and the two to get the parts of the imaged that are colored and above some brightness.
                        Image<Gray, byte> colordetimg = huefilter.And(valfilter);
                        pictureBox2.Image = colordetimg.ToBitmap();

                        //show the image on the screen
                        Bitmap bmp = nextFrame.ToBitmap();
                        pictureBox1.Image = bmp;

                        Bgr c = new Bgr();
                        MCvScalar s = new MCvScalar();
                        nextFrame.AvgSdv(out c, out s);
                        txtHistogram.Text = "R: " + c.Red.ToString();
                        txtHistogram.Text += "G: " + c.Green.ToString();
                        txtHistogram.Text += "B: " + c.Blue.ToString();
                        pictureBox3.BackColor = Color.FromArgb((int)c.Red, (int)c.Green, (int)c.Blue);

                        if (histogramCounter == 0)
                        {
                            //do a color histogram on the image
                            //HistoGram(bmp);
                            histogramCounter = 100;
                        }
                        histogramCounter -= 1;

                        txtHistogramCounter.Text = histogramCounter.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SendLeapData()
        { 
            try
            {
                if (transmitter != null)
                {
                    //SEND OSC DATA
                    decimal leapXRange = numLeapXMax.Value - numLeapXMin.Value;
                    decimal leapYRange = numLeapYMax.Value - numLeapYMin.Value;
                    if (chkLeapOscHand1XSend.Checked && !String.IsNullOrEmpty(txtLeapHand1XPos.Text))
                    {
                        try
                        {
                            decimal leapPos = decimal.Parse(txtLeapHand1XPos.Text);
                            decimal OscRange = numLeapOscHand1XMax.Value - numLeapOscHand1XMin.Value;
                            leapPos -= numLeapXMin.Value;
                            decimal perc = (100 / leapXRange) * leapPos;
                            decimal val = (OscRange / 100) * perc;
                            if (val < numLeapOscHand1XMin.Value) val = numLeapOscHand1XMin.Value;
                            if (val > numLeapOscHand1XMax.Value) val = numLeapOscHand1XMax.Value;
                            numLeapOscHand1XSending.Value = val;
                            OSCMessage msg = new OSCMessage(txtLeapOscHand1XPath.Text, (float)val);
                            transmitter.Send(msg);
                        }
                        catch (Exception ex)
                        {
                            string blah = ex.Message;
                        }
                    }
                    if (chkLeapOscHand1YSend.Checked && !String.IsNullOrEmpty(txtLeapHand1YPos.Text))
                    {
                        try
                        {
                            decimal leapPos = decimal.Parse(txtLeapHand1YPos.Text);
                            decimal OscRange = numLeapOscHand1YMax.Value - numLeapOscHand1YMin.Value;
                            leapPos -= numLeapYMin.Value;
                            decimal perc = (100 / leapYRange) * leapPos;
                            decimal val = (OscRange / 100) * perc;
                            if (val < numLeapOscHand1YMin.Value) val = numLeapOscHand1YMin.Value;
                            if (val > numLeapOscHand1YMax.Value) val = numLeapOscHand1YMax.Value;
                            numLeapOscHand1YSending.Value = val;
                            OSCMessage msg = new OSCMessage(txtLeapOscHand1YPath.Text, (float)val);
                            transmitter.Send(msg);
                        }
                        catch (Exception ex)
                        {
                            string blah = ex.Message;
                        }
                    }
                    if (chkLeapOscHand2XSend.Checked && !String.IsNullOrEmpty(txtLeapHand2XPos.Text))
                    {
                        try
                        {
                            decimal leapPos = decimal.Parse(txtLeapHand2XPos.Text);
                            decimal OscRange = numLeapOscHand2XMax.Value - numLeapOscHand2XMin.Value;
                            leapPos -= numLeapXMin.Value;
                            decimal perc = (100 / leapXRange) * leapPos;
                            decimal val = (OscRange / 100) * perc;
                            if (val < numLeapOscHand2XMin.Value) val = numLeapOscHand2XMin.Value;
                            if (val > numLeapOscHand2XMax.Value) val = numLeapOscHand2XMax.Value;
                            numLeapOscHand2XSending.Value = val;
                            OSCMessage msg = new OSCMessage(txtLeapOscHand2XPath.Text, (float)val);
                            transmitter.Send(msg);
                        }
                        catch (Exception ex)
                        {
                            string blah = ex.Message;
                        }
                    }
                    if (chkLeapOscHand2YSend.Checked && !String.IsNullOrEmpty(txtLeapHand2YPos.Text))
                    {
                        try
                        {
                            decimal leapPos = decimal.Parse(txtLeapHand2YPos.Text);
                            decimal OscRange = numLeapOscHand2YMax.Value - numLeapOscHand2YMin.Value;
                            leapPos -= numLeapYMin.Value;
                            decimal perc = (100 / leapYRange) * leapPos;
                            decimal val = (OscRange / 100) * perc;
                            if (val < numLeapOscHand2YMin.Value) val = numLeapOscHand2YMin.Value;
                            if (val > numLeapOscHand2YMax.Value) val = numLeapOscHand2YMax.Value;
                            numLeapOscHand2YSending.Value = val;
                            OSCMessage msg = new OSCMessage(txtLeapOscHand2YPath.Text, (float)val);
                            transmitter.Send(msg);
                        }
                        catch (Exception ex)
                        {
                            string blah = ex.Message;
                        }
                    }
                                
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void HistoGram(Bitmap bm)
        {
            // Store the histogram in a dictionary          
            Dictionary<Color, int> histo = new Dictionary<Color, int>();
            for (int x = 0; x < bm.Width; x++)
            {
                for (int y = 0; y < bm.Height; y++)
                {
                    // Get pixel color 
                    Color c = bm.GetPixel(x, y);
                    // If it exists in our 'histogram' increment the corresponding value, or add new
                    if (histo.ContainsKey(c))
                        histo[c] = histo[c] + 1;
                    else
                        histo.Add(c, 1);
                }
            }
            txtHistogram.Text = "";
            // This outputs the histogram in an output window
            foreach (Color key in histo.Keys)
            {
                txtHistogram.Text += (key.ToString() + ": " + histo[key]) + "\n";
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try { 
                address = IPAddress.Parse(txtIpAddress.Text);
                port = int.Parse(txtPort.Text);

                if (transmitter != null) transmitter.Close();

                transmitter = new OSCTransmitter(address.ToString(), port);

                //if(osc != null)
                //{
                //    osc.Close();
                //    osc.Dispose();
                //}
                //osc = new OscSender(address, port);
                //osc.Connect();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                chkMouse.Checked = false;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        
        private int Gdc(int a, int b)
        {
            return (b == 0) ? a : Gdc(b, a % b);
        }

        private void lstScreens_SelectedIndexChanged(object sender, EventArgs e)
        {
            //set selected screen
            SelectedScreen = System.Windows.Forms.Screen.AllScreens[lstScreens.SelectedIndex];
        }
        
        private void lstHaarcascade_SelectedIndexChanged(object sender, EventArgs e)
        {
            SetHaarcascade();
        }

        private void SetHaarcascade()
        {
            string path = ((KeyValuePair<string, string>)lstHaarcascade.SelectedItem).Key;
            // adjust path to find your xml
            classifier = new CascadeClassifier(path);
        }

        private void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            timer1.Interval = (int)numRefreshSpeed.Value;
        }

        private void tabPage2_Click(object sender, EventArgs e)
        {

        }

        private void txtOscXPath_TextChanged(object sender, EventArgs e)
        {
            OscXPath = txtOscXPath.Text;
        }

        private void label32_Click(object sender, EventArgs e)
        {

        }

        private void label31_Click(object sender, EventArgs e)
        {

        }

        private void txtOscYPath_TextChanged(object sender, EventArgs e)
        {

            OscYPath = txtOscYPath.Text;
        }


        #region LEAP

        private Controller controller;
        private LeapEventListener listener;

        delegate void LeapEventDelegate(string EventName);
        public void LeapEventNotification(string EventName)
        {
            if (!this.InvokeRequired)
            {
                switch (EventName)
                {
                    case "onInit":
                        Console.WriteLine("Init");
                        break;
                    case "onConnect":
                        this.connectHandler();
                        break;
                    case "onFrame":
                        if (!this.Disposing)
                            this.newFrameHandler(this.controller.Frame());
                        break;
                }
            }
            else
            {
                BeginInvoke(new LeapEventDelegate(LeapEventNotification), new object[] { EventName });
            }
        }

        void connectHandler()
        {
            this.controller.EnableGesture(Gesture.GestureType.TYPE_CIRCLE);
            this.controller.Config.SetFloat("Gesture.Circle.MinRadius", 40.0f);
            this.controller.EnableGesture(Gesture.GestureType.TYPE_SWIPE);
        }

        void newFrameHandler(Frame frame)
        {
            txtLeapFrameId.Text = frame.Id.ToString();
            txtLeapGestureCount.Text = frame.Gestures().Count.ToString();
            txtLeapHands.Text = frame.Hands.Count.ToString();
            Vector leftHandPalmPos = new Vector(0, 0, 0);
            Vector rightHandPalmPos = new Vector(0, 0, 0);
            for(var x = 0; x < frame.Hands.Count; x++)
            {
                if (frame.Hands[x].IsRight)
                {
                    rightHandPalmPos = frame.Hands[x].PalmPosition;
                } else
                {
                    leftHandPalmPos = frame.Hands[x].PalmPosition;
                }
            }
            txtLeapHand1XPos.Text = leftHandPalmPos.x.ToString();
            txtLeapHand1YPos.Text = leftHandPalmPos.y.ToString();
            txtLeapHand1ZPos.Text = leftHandPalmPos.z.ToString();
            txtLeapHand2XPos.Text = rightHandPalmPos.x.ToString();
            txtLeapHand2YPos.Text = rightHandPalmPos.y.ToString();
            txtLeapHand2ZPos.Text = rightHandPalmPos.z.ToString();

            ////The following are Label controls added in design view for the form
            //this.displayID.Text = frame.Id.ToString();
            //this.displayTimestamp.Text = frame.Timestamp.ToString();
            //this.displayFPS.Text = frame.CurrentFramesPerSecond.ToString();
            //this.displayIsValid.Text = frame.IsValid.ToString();
            //this.displayGestureCount.Text = frame.Gestures().Count.ToString();
            //this.displayImageCount.Text = frame.Images.Count.ToString();
        }

        #endregion

        private void button2_Click(object sender, EventArgs e)
        {
            try
            {

                OSCMessage msg = new OSCMessage(txtDirectAddress.Text, txtDirectValue.Text);
                transmitter.Send(msg);
            }
            catch( Exception ex)
            {
                var blah = ex;
            }
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }
    }


    public interface ILeapEventDelegate
    {
        void LeapEventNotification(string EventName);
    }

    public class LeapEventListener : Listener
    {
        ILeapEventDelegate eventDelegate;

        public LeapEventListener(ILeapEventDelegate delegateObject)
        {
            this.eventDelegate = delegateObject;
        }
        public override void OnInit(Controller controller)
        {
            this.eventDelegate.LeapEventNotification("onInit");
        }
        public override void OnConnect(Controller controller)
        {
            this.eventDelegate.LeapEventNotification("onConnect");
        }
        public override void OnFrame(Controller controller)
        {
            this.eventDelegate.LeapEventNotification("onFrame");
        }
        public override void OnExit(Controller controller)
        {
            this.eventDelegate.LeapEventNotification("onExit");
        }
        public override void OnDisconnect(Controller controller)
        {
            this.eventDelegate.LeapEventNotification("onDisconnect");
        }
    }
}
