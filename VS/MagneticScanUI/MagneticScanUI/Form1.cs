﻿using MagneticScanUI.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace MagneticScanUI
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        Bitmap LEDON, LEDOFF;
        Bitmap StatsMap;
        Graphics StatsDraw;
        UInt64 SensorStats = 0;
        UdpClient Search = new UdpClient(0);
        byte[] GetData;
        IPAddress lasttarget;
        IPAddress[] ips;
        int[] WaveSensor;
        int lspeed, rspeed;
        bool updatePID;
        bool updateTurn;
        PathDir LastDir = new PathDir();
        int dircmd;

        int accangle = -0;
        int pathpos;

        Series LSpeed = new Series("L");
        Series RSpeed = new Series("R");
        private bool updateCmd;

        int nodescount = 1;
        PathNode nodes = new PathNode(0,"起点");
        bool NodesSearch = false;
        Stack<PathNode> searchStack = new Stack<PathNode>();
        List<PathType> pathDirList = new List<PathType>();
        List<PathType> PathRunList = new List<PathType>();
        PathNode lastNode;
        PathType lastPath;
        bool TurnBack = false;

        int lastAction;
        int lastPathSelect;
        private bool updatePathSearch;

        MapDraw mapInfo;



        void SearchCheck()
        {
            if (NodesSearch && !updateCmd)
                if (lastAction == 0)
                {
                    if (!TurnBack)
                    {
                        lastPath = (PathType)dircmd;
                        //lastNode[lastPath]=apic
                        dircmd = -1;
                        if ((lastPathSelect & 2) != 0)
                        {
                            lastNode[LastDir.Left] = new PathNode(nodescount++);
                            dircmd = (int)PathType.Left;
                        }
                        if ((lastPathSelect & 1) != 0)
                        {
                            lastNode[LastDir.Forward] = new PathNode(nodescount++);
                            dircmd = (int)PathType.Forward;
                        }
                        if ((lastPathSelect & 4) != 0)
                        {
                            lastNode[LastDir.Right] = new PathNode(nodescount++);
                            dircmd = (int)PathType.Right;
                        }
                        if (dircmd < 0)
                        {
                            lastNode = searchStack.Pop();
                            dircmd = (int)PathType.Back;
                            //lastNode = lastNode[LastDir.Back];
                            LastDir.Rotate(PathType.Back);
                            TurnBack = true;
                        }
                        else
                        {
                            searchStack.Push(lastNode);
                            lastPath = LastDir[(PathType)dircmd];
                            LastDir.Rotate((PathType)dircmd);
                            lastNode[lastPath][LastDir.Back] = lastNode;
                            lastNode = lastNode[lastPath];
                        }
                    }
                    else
                    {
                        dircmd = -1;
                        if ((lastPathSelect & 2) != 0)
                        {
                            if (lastNode[LastDir.Left] != null)
                                if (lastNode[LastDir.Left].isNew)
                                    dircmd = (int)PathType.Left;
                        }
                        if ((lastPathSelect & 1) != 0)
                        {
                            if (lastNode[LastDir.Forward] != null)
                                if (lastNode[LastDir.Forward].isNew)
                                    dircmd = (int)PathType.Forward;
                        }
                        if ((lastPathSelect & 4) != 0)
                        {
                            if (lastNode[LastDir.Right] != null)
                                if (lastNode[LastDir.Right].isNew)
                                    dircmd = (int)PathType.Right;
                        }
                        if (dircmd < 0)
                        {
                            if (searchStack.Count != 0)
                            {
                                lastPath = lastNode[searchStack.Pop()];
                                dircmd = (int)LastDir.getDir(lastPath);
                                lastNode = lastNode[lastPath];
                                LastDir.Rotate((PathType)dircmd);
                            }
                        }
                        else
                        {
                            searchStack.Push(lastNode);
                            lastPath = LastDir[(PathType)dircmd];
                            LastDir.Rotate((PathType)dircmd);
                            lastNode[lastPath][LastDir.Back] = lastNode;
                            lastNode = lastNode[lastPath];
                            TurnBack = false;
                        }
                    }
                    updateCmd = true;
                }
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            LEDON = Resources.LightOn;
            LEDOFF = Resources.LightOff;
            StatsMap = new Bitmap(StatsImg.Width, StatsImg.Height);
            StatsDraw = Graphics.FromImage(StatsMap);
            StatsDraw.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            WaveSensor = new int[2];
            UpdateStats();

            mapInfo = new MapDraw(MapBox.Width, MapBox.Height);

            SpeedChart.Series.Clear();
            LSpeed.ChartType = RSpeed.ChartType = SeriesChartType.Spline;
            LSpeed.BorderWidth = RSpeed.BorderWidth = 2;
            LSpeed.ShadowOffset = RSpeed.ShadowOffset = 2;
            LSpeed.MarkerStyle = MarkerStyle.Diamond;
            RSpeed.MarkerStyle = MarkerStyle.Cross;

            SpeedChart.Series.Add(LSpeed);
            SpeedChart.Series.Add(RSpeed);
            SpeedChart.ChartAreas[0].AxisY.IsStartedFromZero = false;
            SpeedChart.ChartAreas[0].AxisY.Maximum = 100;
            SpeedChart.ChartAreas[0].AxisY.Minimum = -100;

            GetData = Encoding.Default.GetBytes("GetData()");
            ips = getIPAddress();
            new Task(() =>
            {
                while (!this.IsDisposed)
                    try
                    {
                        IPEndPoint p = new IPEndPoint(IPAddress.Any, 0);
                        byte[] buff = Search.Receive(ref p);
                        lasttarget = p.Address;
                        string value = Encoding.Default.GetString(buff);
                        int pos = value.IndexOf("Data:");
                        if (pos != -1)
                        {
                            pos += 5;
                            UInt64 data = 0;
                            BinaryReader br = new BinaryReader(new MemoryStream(buff),Encoding.Default);
                            br.BaseStream.Seek(5, SeekOrigin.Begin);
                            for (int j = 0; j < 8; j++)
                            {
                                data <<= 8;
                                data |= br.ReadByte();
                            }
                            for (int j = 0; j < WaveSensor.Length; j++)
                            {
                                WaveSensor[j] = 0;
                                WaveSensor[j] |= br.ReadByte();
                                WaveSensor[j] <<= 8;
                                WaveSensor[j] |= br.ReadByte();
                            }
                            lspeed = (int)br.ReadByte();
                            if (lspeed > 128) lspeed -= 256;
                            rspeed = (int)br.ReadByte();
                            if (rspeed > 128) rspeed -= 256;
                            int ag = (br.ReadByte()<<8)|br.ReadByte();
                            if (ag > 0x7fff)
                                ag -= 65536;
                            accangle = -ag;
                            ag = (br.ReadByte() << 8) | br.ReadByte();
                            pathpos = ag;
                            lastAction = br.ReadByte();
                            lastPathSelect = br.ReadByte();
                            lastID = br.ReadByte();
                            if (NodesSearch)
                            {
                                if (!updateCmd)
                                {
                                    if (lastAction == 0xff)
                                    {
                                        dircmd = mapInfo.SearchCheck(lastPathSelect);
                                        if (dircmd == -1)
                                        {
                                            NodesSearch = false;
                                            Invoke(new MethodInvoker(() =>
                                            {
                                                SearchButton.BackColor = Color.DarkRed;
                                                PathPoint.Items.Clear();
                                                PathPoint.Items.AddRange(mapInfo.getEndPoint().ToArray());
                                            }));
                                        }
                                        else
                                        {
                                            updateCmd = true;
                                        }
                                        Invoke(new MethodInvoker(() =>
                                        {
                                            MapBox.Image= mapInfo.Update();
                                        }));
                                    }
                                }
                            }
                            //PathNode pn;
                            //data >>= 24;
                            SensorStats = data;
                            Invoke(new MethodInvoker(() =>
                            {
                                UpdateStats();
                            }));
                        }
                    }
                    catch { }

            }).Start();

            FindDev();

            Timer t = new Timer();
            t.Interval = 100;
            t.Tick += new EventHandler((object s, EventArgs ex) =>
            {
                if (lasttarget == null) return;

                Search.Send(GetData, GetData.Length, new IPEndPoint(lasttarget.Address | 0xff000000, 2333));

                if (updatePID)
                {
                    if (sendID == lastID)
                    {
                        updateTurn = false;
                        sendID++;
                        return;
                    }
                    updatePID = false;
                    senddata(0, trackP.Value, trackD.Value, trackI.Value);
                    //byte[] cmd = Encoding.Default.GetBytes("uartpacket(2,{0," + trackP.Value + "," + trackD.Value + "," + trackI.Value + "})");
                    //Search.Send(cmd, cmd.Length, new IPEndPoint(lasttarget.Address | 0xff000000, 2333));
                }
                else if (updateTurn)
                {

                    if (sendID == lastID)
                    {
                        sendID++;
                        updateTurn = false;
                        return;
                    }

                    StringBuilder sb = new StringBuilder();
                    sb.Append("0,0,");
                    sb.Append(PathRunList.Count);
                    sb.Append(',');
                    foreach(PathType type in PathRunList)
                    {
                        switch (type)
                        {
                            case PathType.Forward:
                                sb.Append('2');
                                break;
                            case PathType.Left:
                                sb.Append('0');
                                break;
                            case PathType.Right:
                                sb.Append('1');
                                break;
                            case PathType.Back:
                                sb.Append('3');
                                break;
                        }

                        //switch (str)
                        //{
                        //    case "路口左转":
                        //        sb.Append('0');
                        //        break;
                        //    case "路口右转":
                        //        sb.Append('1');
                        //        break;
                        //    case "路口直走":
                        //        sb.Append('2');
                        //        break;
                        //    case "尽头掉头":
                        //        sb.Append('3');
                        //        break;
                        //    case "复位":
                        //        sb.Append('4');
                        //        break;
                        //}
                        sb.Append(',');
                    }
                    sb.Append("0");
                    senddata(3, sb.ToString());
                    //byte[] cmd = Encoding.Default.GetBytes(sb.ToString());
                    //Search.Send(cmd, cmd.Length, new IPEndPoint(lasttarget.Address | 0xff000000, 2333));
                    

                }
                else if (updateCmd)
                {
                    if (sendID == lastID)
                    {
                        updateCmd = false;
                        sendID++;
                        return;
                    }
                    senddata(4, dircmd);
                    //byte[] cmd = Encoding.Default.GetBytes("uartpacket(2,{4," + sendID + ","+ dircmd + "})");
                    //Search.Send(cmd, cmd.Length, new IPEndPoint(lasttarget.Address | 0xff000000, 2333));
                }   
                else if(updateReset)
                {
                    if (sendID == lastID)
                    {
                        updateReset = false;
                        sendID++;
                        return;
                    }
                    senddata(5);
                    //byte[] cmd = Encoding.Default.GetBytes("uartpacket(2,{5})");
                    //Search.Send(cmd, cmd.Length, new IPEndPoint(lasttarget.Address | 0xff000000, 2333));
                }
                else if(updatePathSearch)
                {
                    if (sendID == lastID)
                    {
                        updatePathSearch = false;
                        sendID++;
                        return;
                    }
                    StringBuilder sb = new StringBuilder();
                    sb.Append("uartpacket(2,{5,0,0,");
                    sb.Append(PathRunList.Count);
                    sb.Append(',');
                    foreach (PathType type in PathRunList)
                    {
                        switch (type)
                        {
                            case PathType.Forward:
                                sb.Append('2');
                                break;
                            case PathType.Left:
                                sb.Append('0');
                                break;
                            case PathType.Right:
                                sb.Append('1');
                                break;
                            case PathType.Back:
                                sb.Append('3');
                                break;
                        }
                        sb.Append(',');
                    }
                    sb.Append("0})");
                    byte[] cmd = Encoding.Default.GetBytes(sb.ToString());
                    Search.Send(cmd, cmd.Length, new IPEndPoint(lasttarget.Address | 0xff000000, 2333));
                }
            });
            t.Start();
        }


        void senddata(int id,params object[] list)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("uartpacket(2,{");
            sb.Append(sendID);
            sb.Append(',');
            sb.Append(id);
            sb.Append(',');
            foreach(object obj in list)
            {
                sb.Append(obj.ToString());
                sb.Append(',');
            }
            sb.Append("0})");
            byte[] cmd = Encoding.Default.GetBytes(sb.ToString());
            Search.Send(cmd, cmd.Length, new IPEndPoint(lasttarget.Address | 0xff000000, 2333));
        }


        void FindDev()
        {
            for (int i = 0; i < ips.Length; i++)
            {
                Search.Send(GetData, GetData.Length, new IPEndPoint(ips[i].Address | 0xff000000, 2333));
            }
        }

        IPAddress[] getIPAddress()
        {
            List<IPAddress> iplist = new List<IPAddress>();
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in nics)
            {
                //判断是否为以太网卡
                //Wireless80211         无线网卡    Ppp     宽带连接
                //Ethernet              以太网卡   
                //这里篇幅有限贴几个常用的，其他的返回值大家就自己百度吧！
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet || adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    if (adapter.Speed < 0) continue;
                    //获取以太网卡网络接口信息
                    IPInterfaceProperties ip = adapter.GetIPProperties();
                    //获取单播地址集
                    UnicastIPAddressInformationCollection ipCollection = ip.UnicastAddresses;
                    foreach (UnicastIPAddressInformation ipadd in ipCollection)
                    {
                        //InterNetwork    IPV4地址      InterNetworkV6        IPV6地址
                        //Max            MAX 位址
                        if (ipadd.Address.AddressFamily == AddressFamily.InterNetwork)
                            //判断是否为ipv4
                            iplist.Add(ipadd.Address);//获取ip
                    }
                }
            }
            return iplist.ToArray();
        }

        private void send_Click(object sender, EventArgs e)
        {
            FindDev();
        }


        private void trackBar1_Scroll(object sender, EventArgs e)
        {
            updatePID=true;
            Pvalue.Text = trackP.Value.ToString();
            Ivalue.Text = trackI.Value.ToString();
            Dvalue.Text = trackD.Value.ToString();
        }

        private void trackBar3_Scroll(object sender, EventArgs e)
        {
            updatePID = true;
        }

        private void turnL_Click(object sender, EventArgs e)
        {
            PathList.Items.Add("路口左转");
        }

        private void turnR_Click(object sender, EventArgs e)
        {
            PathList.Items.Add("路口右转");
        }

        private void SendPath_Click(object sender, EventArgs e)
        {
            PathRunList.Clear();
            foreach (string str in PathList.Items)
            {
                switch (str)
                {
                    case "路口左转":
                        PathRunList.Add(PathType.Left);
                        break;
                    case "路口右转":
                        PathRunList.Add(PathType.Right);
                        break;
                    case "路口直走":
                        PathRunList.Add(PathType.Forward);
                        break;
                    case "尽头掉头":
                        PathRunList.Add(PathType.Back);
                        break;
                    case "复位":
                        PathRunList.Add(PathType.nil);
                        break;
                }
            }
            updateTurn = true;
            updateReset = true;
        }

        private void PathList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                if (PathList.SelectedItem != null)
                {
                    PathList.Items.RemoveAt(PathList.SelectedIndex);
                }
            }
        }

        private void turnF_Click(object sender, EventArgs e)
        {
            PathList.Items.Add("路口直走");
        }

        private void turnB_Click(object sender, EventArgs e)
        {
            PathList.Items.Add(((Control)sender).Text);
        }

        private void trackBar2_Scroll(object sender, EventArgs e)
        {
            updatePID = true;
        }

        int ic = 0;
        int[] testdata = { 1 + 4, 0, 1 + 4, 2, 1 + 2 + 4, 0, 1 + 2 + 4, 0, 1 + 2 + 4, 0, 1 + 2 + 4, 4, 1 + 2 , 4,0,2,1+4};
        private byte lastID=0xff;
        private byte sendID;
        private bool updateReset;

        bool debug = true;

        private void gotoNodeButton_Click(object sender, EventArgs e)
        {
            if (PathPoint.SelectedItem != null)
            {
                //List<PathNode> ns = 
                if (mapInfo.MoveToPoint((PathNode)PathPoint.SelectedItem))
                {
                    PathRunList.Clear();
                    List<PathType> list = mapInfo.getTargetPath();
                    dircmd = (int)list[0];
                    if (list[0] == PathType.Forward)
                        list.RemoveAt(0);
                    list.Add(PathType.Forward);
                    PathRunList.AddRange(list.ToArray());
                    updateTurn = true;
                    updateCmd = true;
                    //updateReset = true;
                }
                MapBox.Image = mapInfo.Update();
                
                //PathDir dir = mapInfo.LastPathDir;
                //List<PathType> tplist = new List<PathType>();
                //PathNode lastNode=null;
                //foreach(PathNode n in ns)
                //{
                //    PathType type;
                //    if (lastNode == null)
                //    {
                //        lastNode = n;
                //        continue;
                //    }
                //    type = dir.getDir(lastNode[n]);
                //    tplist.Add(type);
                //    dir.Rotate(type);
                //    lastNode = n;
                //}
            }
        }

        private void PathPoint_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (PathPoint.SelectedItem != null)
            {
                mapInfo.setTargetPoint((PathNode)PathPoint.SelectedItem);
                MapBox.Image = mapInfo.Update();
            }
        }

        private void upkey_Click(object sender, EventArgs e)
        {
            dircmd = (int)PathType.Forward;
            updateCmd = true;
        }

        private void leftkey_Click(object sender, EventArgs e)
        {
            dircmd = (int)PathType.Left;
            updateCmd = true;
        }

        private void downkey_Click(object sender, EventArgs e)
        {
            dircmd = (int)PathType.Back;
            updateCmd = true;
        }

        private void rightkey_Click(object sender, EventArgs e)
        {
            dircmd = (int)PathType.Right;
            updateCmd = true;
        }

        private void resetKey_Click(object sender, EventArgs e)
        {
            updateReset = true;
        }

        SaveFileDialog sf = new SaveFileDialog();
        private void SaveMap_Click(object sender, EventArgs e)
        {
            sf.Filter = "地图数据(*.map)|*.map";
            if (sf.ShowDialog() == DialogResult.OK)
            {
                FileStream fs = new FileStream(sf.FileName, FileMode.OpenOrCreate);
                mapInfo.toBin(fs);
                fs.Flush();
                fs.Close();
            }
        }

        OpenFileDialog of = new OpenFileDialog();
        private void LoadMap_Click(object sender, EventArgs e)
        {
            of.Filter = "地图数据(*.map)|*.map";
            if (of.ShowDialog() == DialogResult.OK)
            {
                FileStream fs = new FileStream(of.FileName, FileMode.Open);
                mapInfo.setNode( mapInfo.toNode(fs));
                fs.Close();
                PathPoint.Items.Clear();
                PathPoint.Items.AddRange(mapInfo.getEndPoint().ToArray());
                MapBox.Image = mapInfo.Update();
                //mapInfo.
            }
        }

        private void SearchButton_Click(object sender, EventArgs e)
        {
            if (debug)
            {
                if (ic == 0)
                    mapInfo.SearchInit();
                if (mapInfo.SearchCheck(testdata[ic++]) == -1)
                {
                    PathPoint.Items.Clear();
                    PathPoint.Items.AddRange(mapInfo.getEndPoint().ToArray());
                }
                MapBox.Image = mapInfo.Update();
                if (ic == testdata.Length) ic = 0; ;
                return;
            }
            if (NodesSearch)
            {
                ((Control)sender).BackColor = Color.DarkRed;
                NodesSearch = false;
                return;
            }
            ((Control)sender).BackColor = Color.LightGreen;
            mapInfo.SearchInit();
            NodesSearch = true;
            dircmd = (int)PathType.Forward;
            updateReset = true;
            MapBox.Image = mapInfo.Update();
            
        }


        void UpdateStats()
        {
            UInt64 temp = (UInt64)1 << 7;
            double dy;
            double dx;
            StatsDraw.Clear(Color.White);
            for (int i = 0; i <9; i++)
            {
                dy = Math.Sin((i * 15 + -150) * Math.PI / 180) * 140 + 160;
                dx = Math.Cos((i * 15 + -150) * Math.PI / 180) * 140 + 160;
                int x = (int)dx;
                int y = (int)dy;
                if ((SensorStats & temp) != 0)
                {
                    StatsDraw.DrawImage(LEDOFF, x - 16, y - 16, 24, 24);
                }
                else
                {
                    StatsDraw.DrawImage(LEDON, x - 16, y - 16, 24, 24);
                }
                temp <<= 1;
            }
            Point[] ps = new Point[4];
            dy = Math.Sin((accangle - 90 -5) * Math.PI / 180) * 45 + 160;
            dx = Math.Cos((accangle - 90 -5) * Math.PI / 180) * 45 + 160;
            ps[0].X = (int)dx;
            ps[0].Y = (int)dy;
            dy = Math.Sin((accangle - 90) * Math.PI / 180) * 50 + 160;
            dx = Math.Cos((accangle - 90) * Math.PI / 180) * 50 + 160;
            ps[1].X = (int)dx;
            ps[1].Y = (int)dy;
            dy = Math.Sin((accangle - 90+6) * Math.PI / 180) * 45 + 160;
            dx = Math.Cos((accangle - 90+6) * Math.PI / 180) * 45 + 160;
            ps[2].X = (int)dx;
            ps[2].Y = (int)dy;

            ps[3].X = 160;
            ps[3].Y = 160;

            StatsDraw.FillEllipse(Brushes.Black, new Rectangle(160 - 64, 160 - 64, 128, 128));
            //StatsDraw.DrawLine(Pens.White, new Point(160, 160), new Point((int)dx, (int)dy));
            StatsDraw.FillPolygon(Brushes.Wheat, ps);
            StatsDraw.FillEllipse(Brushes.White, new Rectangle(160 - 5, 160 - 5, 10, 10));
            StatsDraw.FillRectangle(Brushes.White, new Rectangle(160 - 8, 160 + 64 + 8, 32, 16));
            StatsDraw.DrawString(accangle.ToString() + "°", Font, Brushes.Black, new Point(160 - 8, 160 + 64 +8));
            //StatsDraw.DrawEllipse(Pens.Black, 142-8, 142-8, 200+16, 200+16);
            StatsImg.Image = StatsMap;

            FLen.Text = WaveSensor[0] + "";
            BLen.Text = WaveSensor[1] + "";

            LSpeed.Points.AddY(lspeed * 3.125);
            RSpeed.Points.AddY(rspeed * 3.125);

            if (LSpeed.Points.Count >= 100)
            {
                LSpeed.Points.RemoveAt(0);
                RSpeed.Points.RemoveAt(0);
                //for(int i = 0; i < 300; i++)
                //{
                //    ser.Points[i].SetValueY((ser.Points[i * 2].YValues[0]+ ser.Points[i * 2+1].YValues[0])/2);
                //}
                //for (int i = 300; i < 600; i++)
                //    ser.Points.RemoveAt(300);
            }
            if(codeindex.Checked)
                if (PathList.Items.Count> pathpos)
                {
                    PathList.SelectedIndex = pathpos;
                }
        }



        

    }
}
