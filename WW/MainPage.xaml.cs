/*
 * Author: Tianye Zhao
 * Date: Nov 02,2018
 *
 * Requirement
 * Hardware: Raspberry PI 3 and SenseHat
 * System: Window 10 IoT Core
 * 
 * This program is design for CS501 IoT project.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using DreamTimeStudioZ.Recipes;
using Windows.Storage;
using Emmellsoft.IoT.Rpi.SenseHat;
using Emmellsoft.IoT.Rpi.SenseHat.Fonts.SingleColor;
using Windows.UI;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace WW
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private enum States
        {
            STARTUP, LIGHT_SHOW_1, GET_FORECAST, ASK_TEMP, ASK_FORECAST, LIGHT_SHOW_2, MONITOR_DAY, MONITOR_NIGHT, WEATHER_ERROR
        };

        private String[] stateNames = { "STARTUP", "LIGHT_SHOW_1", "GET_FORECAST", "ASK_TEMP", "ASK_FORECAST", "LIGHT_SHOW_2", "MONITOR_DAY", "MONITOR_NIGHT", "WEATHER_ERROR" };

        private States currentState = States.STARTUP;
        private Boolean stateChanged = true;
        private int cycleCount = 0;
        private int ls1StartCycle;
        private int ls2StartCycle;

        private String forecastHighTemp;
        private String forecastLowTemp;
        private String forecastToConfirm;
        private DateTime sunrise;
        private DateTime sunset;

        private Boolean askTempInitialized = false;
        private Boolean userHasMadeTempChoice = false;
        private Boolean userAgreeWithTemp = false;
        private String tempToConfirm;

        private Boolean askForecastInitialized = false;
        private Boolean userHasMadeForecastChoice = false;
        private Boolean userAgreeWithForecast = false;

        // button states for sense hat
        private enum ButtonStates
        {
            UNKNOWN, LEFT, RIGHT, UP, DOWN, ENTER
        };
        private ButtonStates lastButtonState = ButtonStates.UNKNOWN; // keep record of which button is pressed last time
        private Boolean keyStateChanged = false;

        // create log file for entries
        StorageFile logFile;
        private StorageFolder logFolder = Windows.Storage.ApplicationData.Current.LocalFolder;

        // create timer
        private DispatcherTimer timer1;
        private DispatcherTimer timer2;
        private Boolean inTimerTick = false; // flag of if there is a task in processing
        private bool appInitialized = false; // flag of if app fully initialized

        private int ambTemp;

        private ISenseHat senseHat; // access to sensehat library
        private ISenseHatDisplay display;
        private TinyFont tinyFont; // font to write text on display
        private Color textColor = Colors.White;
        private String text;

        private Boolean waitingForResponse = false;

        public MainPage()
        {
            this.InitializeComponent();
            inTimerTick = false;
            timer1 = new DispatcherTimer();
            timer1.Interval = TimeSpan.FromMilliseconds(15);
            timer1.Tick += Timer_Tick1;

            timer2 = new DispatcherTimer();
            timer2.Interval = TimeSpan.FromMilliseconds(4000);
            timer2.Tick += Timer_Tick2;

            Run();
        }

        // run loops
        void Run()
        {
            Initialize();
            timer1.Start(); // start timer
            timer2.Start();
        }

        // initialization
        async void Initialize()
        {
            senseHat = await SenseHatFactory.GetSenseHat().ConfigureAwait(false); // access to sensehat
            display = senseHat.Display; // access to sensehat display

            logFile = await logFolder.CreateFileAsync("log.txt",
                Windows.Storage.CreationCollisionOption.ReplaceExisting); // if log file already exists replace existing

            writeToLog("Weather Awareness 0.1\r\n---------------\r\nInitialized " + DateTime.Now.ToString());
            writeToLog(DateTime.Now.TimeOfDay.ToString());

            appInitialized = true;
        }

        // timer callbacks for states changing
        private void Timer_Tick1(object send, object e)
        {
            if (!appInitialized) // check initialization status in case crashing
                return;

            if (inTimerTick) // check if it is in processing
                return;

            inTimerTick = true; // start processing

            tinyFont = new TinyFont();

            Boolean oldKSC = keyStateChanged;
            keyStateChanged = senseHat.Joystick.Update(); // check if any button is pressed
            if (keyStateChanged && !oldKSC) // de-bounce duplicate key presses
                HandleButtons();

            cycleCount++;

            switch (currentState)
            {
                case States.STARTUP:
                    StartupState();
                    break;

                case States.LIGHT_SHOW_1:
                    LightShow1State();
                    break;

                case States.GET_FORECAST:
                    GetForecastState();
                    break;

                case States.ASK_TEMP:
                    AskTempState();
                    break;

                case States.ASK_FORECAST:
                    AskForecastState();
                    break;

                case States.LIGHT_SHOW_2:
                    LightShow2State();
                    break;

                case States.MONITOR_DAY:
                    MonitorDayState();
                    break;

                case States.MONITOR_NIGHT:
                    MonitorNightState();
                    break;

                case States.WEATHER_ERROR:
                    WeatherErrorState();
                    break;
            }

            inTimerTick = false; // end processing
        }

        // timer callbacks for temperature monitoring
        private async void Timer_Tick2(object sender, object e)
        {
            if (senseHat == null)
                return;

            // get ambient temperature from sensehat and write to log
            senseHat.Sensors.HumiditySensor.Update();

            if (senseHat.Sensors.Temperature.HasValue)
            {
                double temp = senseHat.Sensors.Temperature.Value;
                ambTemp = (int)Math.Round(temp) - 10; // temperature around sensor is higher than environment
                writeToLog("Current ambient tempeature: " + ambTemp.ToString());
            }

            string str = await AzureIoTHub.ReceiveCloudToDeviceMessageAsync();
            if (str != null)
                writeToLog("Message from Azure IoT hub: " + str);
        }

        // states
        private void StartupState()
        {
            currentState = States.LIGHT_SHOW_1;
            stateChanged = true;
            LogStateChange();
        }

        // write state change to log
        private void LogStateChange()
        {
            String state = stateNames[Convert.ToInt32(currentState)];
            writeToLog("Current state: " + state);
        }

        private void LightShow1State()
        {
            int cycle;
            Color newBGColor;
            
            if (stateChanged)
            {
                ls1StartCycle = cycleCount;
                stateChanged = false;
            }
            else
            {
                cycle = cycleCount - ls1StartCycle;
                Random rnd = new Random();
                int rValue;
                int gValue;
                int bValue;

                for (int x = 0; x < 8; x++)
                {
                    for (int y = 0; y < 8; y++)
                    {
                        rValue = rnd.Next(0, 255);
                        gValue = rnd.Next(0, 255);
                        bValue = rnd.Next(0, 255);
                        newBGColor = Color.FromArgb(255, (byte)rValue, (byte)gValue, (byte)bValue);

                        display.Screen[x, y] = newBGColor;
                    }
                }
                display.Update();

                if (cycle > 40)
                {
                    currentState = States.GET_FORECAST;
                    stateChanged = true;
                    LogStateChange();
                }
            }
        }

        // get data from openweathermap api
        private async void GetForecastState()
        {
            string forecastSymbol;
            string forecastIcon;
            string cityID = "6146143"; // extract from city id list
            string appID = "60605dc0c4ea32dc8351a1a32fff72a4"; // registered id
            string url;


            if (waitingForResponse) // call this only once even if network delay or error
                return;

            display.Fill(Colors.Orange);
            display.Update();

            // extract weather forecast data from openweathermap in xml format and in celsius degree for next 3 hours
            url = "http://api.openweathermap.org/data/2.5/forecast?id=" + cityID + "&mode=xml&units=metric&cnt=1&APPID=" + appID;

            HttpClient client = new HttpClient();
            waitingForResponse = true;

            HttpResponseMessage response = await client.GetAsync(url);
            String xmlContent = await response.Content.ReadAsStringAsync();
            XmlDocument xDocument = new XmlDocument();
            xDocument.LoadXml(xmlContent);

            forecastHighTemp = "Unknown";
            forecastLowTemp = "Unknown";
            forecastSymbol = "Unknown";
            forecastIcon = "Unknown";

            // get low and high temperature
            XmlNode tempNode = xDocument.SelectSingleNode("//temperature");
            XmlAttribute tempValue = tempNode.Attributes["max"];
            // round result and display digits without "-" sign when temp < -9
            // another option is using fahrenneit by substituting "&units=metric" with "&units=imperial" in url
            int temp = (int)Math.Round(double.Parse(tempValue.Value));
            forecastHighTemp = temp > -10 ? temp.ToString() : Math.Abs(temp).ToString();
            tempValue = tempNode.Attributes["min"];
            temp = (int)Math.Round(double.Parse(tempValue.Value));
            forecastLowTemp  = temp > -10 ? temp.ToString() : Math.Abs(temp).ToString();

            // get weather symbol
            tempNode = xDocument.SelectSingleNode("//symbol");
            tempValue = tempNode.Attributes["name"];
            forecastSymbol = tempValue.Value;
            tempValue = tempNode.Attributes["var"];
            forecastIcon = tempValue.Value;

            // get sunrise and sunset
            tempNode = xDocument.SelectSingleNode("//sun");
            tempValue = tempNode.Attributes["rise"];
            sunrise = Convert.ToDateTime(tempValue.Value).AddHours(-5); // change to eastern time
            tempValue = tempNode.Attributes["set"];
            sunset = Convert.ToDateTime(tempValue.Value).AddHours(-5);

            forecastToConfirm = forecastIcon.Substring(0, 2); // extract weather icon

            writeToLog("Forecast: H(" + forecastHighTemp + ") L(" + forecastLowTemp + ") " + forecastSymbol +
                "\r\nSunrise (" + sunrise.TimeOfDay.ToString() + ") Sunset (" + sunset.TimeOfDay.ToString() + ")");

            display.Fill(Colors.Purple);
            display.Update();
            if (forecastLowTemp != "Unknown" && forecastHighTemp != "Unknown")
            {
                currentState = States.ASK_TEMP;
                askTempInitialized = false;
                stateChanged = true;
                LogStateChange();
            }
            else
            {
                currentState = States.WEATHER_ERROR;
                stateChanged = true;
                LogStateChange();
            }
            waitingForResponse = false;
            writeToLog("Forecast: " + forecastSymbol);

            await AzureIoTHub.SendDeviceToCloudMessageAsync("Forecast: L (" + forecastLowTemp + "), H (" + forecastHighTemp + "), "+ forecastSymbol);
        }

        // check if it is daytime now
        private Boolean isDayTime()
        {
            Boolean result = true;
            if (TimeSpan.Compare(DateTime.Now.TimeOfDay, sunrise.TimeOfDay) < 0 ||
                TimeSpan.Compare(DateTime.Now.TimeOfDay, sunset.TimeOfDay) > 0)
                result = false;

            return result;
        }

        private void AskTempState()
        {
            if (!askTempInitialized)
            {
                if (isDayTime())
                {
                    tempToConfirm = forecastHighTemp;
                }
                else
                {
                    tempToConfirm = forecastLowTemp;
                }
                userHasMadeTempChoice = false;
                userAgreeWithTemp = false;
                display.Clear();
                display.Fill(Colors.Black);
                tinyFont.Write(display, tempToConfirm, textColor);
                display.Update();
                askTempInitialized = true;
            }
            else
            {
                if (keyStateChanged)
                {
                    // ask user to agree (up/down with green) or disagree(left/right with red) temperature forecast
                    if ((lastButtonState == ButtonStates.LEFT) || (lastButtonState == ButtonStates.RIGHT))
                    {
                        userAgreeWithTemp = false;
                        userHasMadeTempChoice = true;
                        display.Clear();
                        display.Fill(Colors.Red);
                        tinyFont.Write(display, tempToConfirm, textColor);
                        display.Update();
                    }
                    else if ((lastButtonState == ButtonStates.UP) || (lastButtonState == ButtonStates.DOWN))
                    {
                        userAgreeWithTemp = true;
                        userHasMadeTempChoice = true;
                        display.Clear();
                        display.Fill(Colors.Green);
                        tinyFont.Write(display, tempToConfirm, textColor);
                        display.Update();
                    }
                    else
                    {
                        if (!userHasMadeTempChoice)
                            return;

                        currentState = States.ASK_FORECAST;
                        askForecastInitialized = false;
                        stateChanged = true;
                        LogStateChange();
                    }
                }
            }
        }

        private void AskForecastState()
        {
            if (!askForecastInitialized)
            {
                userHasMadeForecastChoice = false;
                userAgreeWithForecast = false;
                display.Clear();
                display.Fill(Colors.Black);

                // check weather icon data with openweathermap icon
                if (forecastToConfirm == "01") // clear sky
                {
                    text = "++";
                    textColor = Colors.Orange;
                }
                else if (forecastToConfirm == "02") // few clounds
                {
                    text = "--";
                    textColor = Colors.Yellow;
                }
                else if (forecastToConfirm == "03") // scattered clouds
                {
                    text = "==";
                    textColor = Colors.LightGray;
                }
                else if (forecastToConfirm == "04") // broken clouds
                {
                    text = "%%";
                    textColor = Colors.Gray;
                }
                else if (forecastToConfirm == "09") // shower rain
                {
                    text = "''";
                    textColor = Colors.LightBlue;
                }
                else if (forecastToConfirm == "10") // rain
                {
                    text = "::";
                    textColor = Colors.Blue;
                }
                else if (forecastToConfirm == "11") // thunderstorm
                {
                    text = "//";
                    textColor = Colors.DarkBlue;
                }
                else if (forecastToConfirm == "13") // snow
                {
                    text = "**";
                    textColor = Colors.AliceBlue;
                }
                else if (forecastToConfirm == "50") // mist
                {
                    text = "..";
                    textColor = Colors.WhiteSmoke;
                }

                tinyFont.Write(display, text, textColor);
                display.Update();
                askForecastInitialized = true;
            }
            else
            {
                if (keyStateChanged)
                {
                    // ask user to agree (up/down with green) or disagree (left/right with red) symbol forecast
                    if ((lastButtonState == ButtonStates.LEFT) || (lastButtonState == ButtonStates.RIGHT))
                    {
                        userAgreeWithForecast = false;
                        userHasMadeForecastChoice = true;
                        display.Clear();
                        display.Fill(Colors.Red);
                        tinyFont.Write(display, text, textColor);
                        display.Update();
                    }
                    else if ((lastButtonState == ButtonStates.UP) || (lastButtonState == ButtonStates.DOWN))
                    {
                        userAgreeWithForecast = true;
                        userHasMadeForecastChoice = true;
                        display.Clear();
                        display.Fill(Colors.Green);
                        tinyFont.Write(display, text, textColor);
                        display.Update();
                    }
                    else
                    {
                        if (!userHasMadeForecastChoice)
                            return;

                        currentState = States.LIGHT_SHOW_2;
                        stateChanged = true;
                        LogStateChange();
                    }
                }
            }
        }

        private void LightShow2State()
        {
            int cycle;
            Color newBGColor;
            textColor = Colors.White;

            if (stateChanged)
            {
                ls2StartCycle = cycleCount;
                stateChanged = false;
            }
            else
            {
                cycle = cycleCount - ls2StartCycle;
                Random rnd = new Random();
                int rValue;
                int gValue;
                int bValue;

                for (int x = 0; x < 8; x++)
                {
                    for (int y = 0; y < 8; y++)
                    {
                        rValue = rnd.Next(0, 255);
                        gValue = rnd.Next(0, 255);
                        bValue = rnd.Next(0, 255);
                        newBGColor = Color.FromArgb(255, (byte)rValue, (byte)gValue, (byte)bValue);

                        display.Screen[x, y] = newBGColor;
                    }
                }
                display.Update();

                if (cycle > 60)
                {
                    if (isDayTime())
                    {
                        currentState = States.MONITOR_DAY;
                    }
                    else
                    {
                        currentState = States.MONITOR_NIGHT;
                    }
                    
                    stateChanged = true;
                    LogStateChange();
                }
            }
        }

        private void MonitorDayState()
        {
            if (!isDayTime())
            {
                currentState = States.MONITOR_NIGHT;
                stateChanged = true;
                LogStateChange();
            }
            else
            {
                // if ambient temperature >= forecastHighTemp then forecast is right with yellowgreen and wrong with firebrick
                if (ambTemp >= Convert.ToInt32(forecastHighTemp))
                {
                    display.Fill(Colors.YellowGreen);
                }
                else
                {
                    display.Fill(Colors.Firebrick);
                }

                tinyFont.Write(display, ambTemp.ToString(), textColor);
                display.Update();

                // continue runing after 5000 cycles
                if (cycleCount % 5000 == 0)
                {
                    currentState = States.LIGHT_SHOW_2;
                    stateChanged = true;
                    LogStateChange();
                }
            }
        }

        private void MonitorNightState()
        {
            if (isDayTime())
            {
                currentState = States.MONITOR_DAY;
                stateChanged = true;
                LogStateChange();
            }
            else
            {
                // if ambient temperature >= forecastHighTemp then forecast is right or wrong with colors
                if (ambTemp <= Convert.ToInt32(forecastLowTemp))
                {
                    display.Fill(Colors.Brown);
                }
                else
                {
                    display.Fill(Colors.Chartreuse);
                }

                tinyFont.Write(display, ambTemp.ToString(), textColor);
                display.Update();

                // continue runing after 500 cycles
                if (cycleCount % 500 == 0)
                {
                    currentState = States.LIGHT_SHOW_2;
                    stateChanged = true;
                    LogStateChange();
                }
            }
        }

        // display error with "??" and yellow if no data collected from api
        private void WeatherErrorState()
        {
            display.Fill(Colors.MistyRose);
            tinyFont.Write(display, "??", Colors.Yellow);
            display.Update();
        }

        // determine which key is pressed on joystick
        private void HandleButtons()
        {
            var joy = senseHat.Joystick;

            if(joy.EnterKey.IsPressed())
                lastButtonState = ButtonStates.ENTER;

            if (joy.LeftKey.IsPressed())
                lastButtonState = ButtonStates.LEFT;

            if (joy.RightKey.IsPressed())
                lastButtonState = ButtonStates.RIGHT;

            if (joy.UpKey.IsPressed())
                lastButtonState = ButtonStates.UP;

            if (joy.DownKey.IsPressed())
                lastButtonState = ButtonStates.DOWN;
        }

        // write entries to log file with IORecipes
        async void writeToLog(String str)
        {
            // check initialization status in case crashing
            if (appInitialized)
                textBox.Text += "\r\n" + str;

            logFile = await logFolder.GetFileAsync("log.txt"); // open file

            // try to get the file 10 times in case crashing
            int i = 0;
            while(!(await IORecipes.WriteStringToFileWithAppendOption(logFile, str + "\r\n", true)) && (i < 10))
                i++;
        }

        // enable textbox to scroll down
        private void textBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // use tree helpers to get down to find the actual scroll viewer and let scroll viewer scroll down to the bottom
            var grid = (Grid)VisualTreeHelper.GetChild(textBox, 0);
            for (var i = 0; i <= VisualTreeHelper.GetChildrenCount(grid) - 1; i++)
            {
                object obj = VisualTreeHelper.GetChild(grid, i);
                if (!(obj is ScrollViewer)) continue;
                ((ScrollViewer)obj).ChangeView(0.0f, ((ScrollViewer)obj).ExtentHeight, 1.0f);
                break;
            }
        }
    }
}