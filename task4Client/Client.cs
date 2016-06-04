﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using task4Lib;
using System.Diagnostics;
using Message = task4Lib.Message;
namespace task4Client
{
    public delegate void UpdateControlsDelegate(bool OnConnect);
    public delegate void MessageDelegate(Message msg);
    public delegate void OnlineOfflineDelegate(Message msg);
    public delegate void FileInfoDelegate(Message msg);
    public delegate void StatusDelegate(string str);
    public delegate void SimpleDelegate();
    public delegate void ExceptionDelegate(Exception ex);
    public delegate void SocketErrorDelegate(int id);

    public partial class Client : Form
    {
        Socket client;
        private ManualResetEvent mreFile;
        ManualResetEvent mreMessage;
        ManualResetEvent mreSend;
        UserInformation SelfInfo;
        UserInformation ServerInfo;
        IPAddress ServerIP;
        IPEndPoint hostEndPoint;
        Thread connection;
        Thread threadMessageProcess;
        ClientMessageProcess MsgProcess;
        Queue<Message> msgQueue;
        Dictionary<int, UserInformation> friends;

        public Client()
        {
            InitializeComponent();
            this.FormClosed += Client_FormClosed;
            mreFile = new ManualResetEvent(false);
            textBoxIP.Text = IPAddress.Loopback.ToString();
            msgQueue = new Queue<Message>();
            mreMessage = new ManualResetEvent(false);
            mreSend = new ManualResetEvent(false);

            friends = new Dictionary<int, UserInformation>();

            SelfInfo = new UserInformation();
            ServerInfo = new UserInformation(UserInformation.ServerID);
            MsgProcess = new ClientMessageProcess(SelfInfo, msgQueue, friends, mreMessage);

            MsgProcess.OnlineNotifyReceived += MsgProcess_OnlineNotifyReceived;
            MsgProcess.OfflineNotifyReceived += MsgProcess_OfflineNotifyReceived;
            MsgProcess.MessageReceived += MsgProcess_MessageReceived;
            MsgProcess.FileInfoMessageReceived += MsgProcess_FileInfoMessageReceived;
            MsgProcess.MessageProcessErrorOccurred += MsgProcess_MessageProcessErrorOccurred;

            listViewChat.Columns.Add("用户", listViewChat.Width / 3);
            listViewChat.Columns.Add("内容", listViewChat.Width);

            listViewOnline.Columns.Add("用户", listViewOnline.Width * 2 / 3);
            listViewOnline.Columns.Add("ID", listViewOnline.Width / 3);
        }

        void Client_FormClosed(object sender, FormClosedEventArgs e)
        {
            Process.GetCurrentProcess().Kill();
        }


        #region 消息处理产生错误
        void MsgProcess_MessageProcessErrorOccurred(object sender, MessageProcessErrorArgs e)
        {
            if (InvokeRequired)
            {
                this.BeginInvoke(new ExceptionDelegate(MessageProcessErrorOccurred), e.InnerException);
            }
            else
            {
                this.MessageProcessErrorOccurred(e.InnerException);
            }
        }
        public void MessageProcessErrorOccurred(Exception ex)
        {
            MessageBox.Show(ex.ToString());
        }
        #endregion

        #region 文件处理
        void MsgProcess_FileInfoMessageReceived(object sender, MessageEventArgs e)
        {
            if (InvokeRequired)
                this.BeginInvoke(new FileInfoDelegate(this.OnFileInfoReceived), e.Msg);

            else
            {
                OnFileInfoReceived(e.Msg);
            }
        }
        private void OnFileInfoReceived(Message msg)
        {
            new Thread(delegate() { new FileReceiverForm(msg).ShowDialog(); }).Start();
        }
        #endregion

        #region 消息处理
        void MsgProcess_MessageReceived(object sender, MessageEventArgs e)
        {
            if (InvokeRequired)
                this.BeginInvoke(new MessageDelegate(this.OnMessageReceived), e.Msg);

            else
            {
                this.OnMessageReceived(e.Msg);
            }
        }
        void OnMessageReceived(Message msg)
        {
            ListViewItem item = new ListViewItem();
            item.Text = msg.Sender.Name;
            item.SubItems.Add(msg.Content.ToString());
            listViewChat.Items.Add(item);
        }
        #endregion

        #region 上线处理
        void MsgProcess_OnlineNotifyReceived(object sender, MessageEventArgs e)
        {
            if (InvokeRequired)
                this.BeginInvoke(new OnlineOfflineDelegate(this.OnOnlineNotifyReceived), e.Msg);
            else
            {
                this.OnOnlineNotifyReceived(e.Msg);
            }
        }

        private void OnOnlineNotifyReceived(Message msg)
        {
            foreach (var friend in msg.Friends)
            {
                lock (this.friends)
                {
                    if (!this.friends.ContainsKey(friend.Key))
                        this.friends.Add(friend.Key, friend.Value);
                }
                ListViewItem item = new ListViewItem();
                item.Text = friend.Value.Name;
                item.SubItems.Add(friend.Value.ID.ToString());
                listViewOnline.Items.Add(item);
            }

        }
        #endregion

        #region 下线处理
        void MsgProcess_OfflineNotifyReceived(object sender, MessageEventArgs e)
        {
            if (InvokeRequired)
                this.BeginInvoke(new OnlineOfflineDelegate(this.OnOfflineNotifyReceived), e.Msg);

            else
            {
                OnOfflineNotifyReceived(e.Msg);
            }
        }
        private void OnOfflineNotifyReceived(Message msg)
        {
            string strID = msg.Friend.ID.ToString();
            foreach (ListViewItem item in listViewOnline.Items)
            {
                if (item.SubItems[1].Text == strID)
                {
                    listViewOnline.Items.Remove(item);
                    return;
                }
            }
        }
        #endregion

        public void ExceptionOccurred(Exception ex)
        {
            if (InvokeRequired)
                this.Invoke(new ExceptionDelegate(ExceptionOccurred), ex);
            else
            {
                MessageBox.Show(ex.ToString());
                this.Clean();
            }
        }

        private void UpdateStatus(string str)
        {
            status.Text = str;
        }
        private void btnConnectServer_Click(object sender, EventArgs e)
        {
            if (!IPAddress.TryParse(textBoxIP.Text, out ServerIP))
            {
                MessageBox.Show("IP地址错误", "错误");
                return;
            }
            if (textBoxClientName.Text == string.Empty)
            {
                MessageBox.Show("用户名不能为空", "错误");
                return;
            }
            SelfInfo.Name = textBoxClientName.Text;
            try
            {
                ServerInfo.Port = Convert.ToInt32(textBoxPort.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show("端口号错误", "错误");
                return;
            }
            if (ServerInfo.Port <= 0)
            {
                MessageBox.Show("端口号错误", "错误");
                return;
            }
            try
            {
                hostEndPoint = new IPEndPoint(ServerIP, ServerInfo.Port);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "错误");
                return;
            }
            connection = new Thread(ConnectServer);
            connection.IsBackground = true;
            connection.Start();
            threadMessageProcess = new Thread(MsgProcess.StartMessageProcess);
            threadMessageProcess.IsBackground = true;
            threadMessageProcess.Start();
            UpdateControls(true);
        }

        private void ConnectServer()
        {
            try
            {
                client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                client.SendBufferSize = Message.MaxMessageSize;
                client.ReceiveBufferSize = Message.MaxMessageSize;
                client.BeginConnect(hostEndPoint, new AsyncCallback(OnConnected), client);
            }
            catch (Exception e)
            {
                if (InvokeRequired)
                {
                    this.BeginInvoke(new ExceptionDelegate(ConnectFailed), e);
                }
                else
                {
                    this.ConnectFailed(e);
                }
            }

        }

        public void ConnectFailed(Exception e)
        {
            MessageBox.Show(e.ToString(), "连接失败");
            UpdateControls(false);
        }

        public void OnConnected(IAsyncResult ar)
        {

            Socket client = (Socket)ar.AsyncState;


            byte[] data = null;
            try
            {
                client.EndConnect(ar);
                data = Message.GetMessageBytes(SelfInfo, ServerInfo, "你好，我是 " + SelfInfo.Name);
                client.Send(data, 0, data.Length, SocketFlags.None);
                data = new byte[Message.MaxMessageSize];
                client.Receive(data, 0, data.Length, SocketFlags.None);
            }
            catch (Exception e)
            {
                ExceptionOccurred(e);
                return;
            }
            Message msg = new Message();
            try
            {
                msg.ParseXmlString(data);
            }
            catch(Exception ex)
            {
                ExceptionOccurred(ex);
                return;
            }
            lock (msgQueue)
            {
                msgQueue.Enqueue(msg);
                mreMessage.Set();
            }
            this.SelfInfo.ID = msg.Receiver.ID;

            while (true)
            {
                data = new byte[Message.MaxMessageSize];
                try
                {
                    client.Receive(data, 0, data.Length, SocketFlags.None);
                    msg = new Message();
                    msg.ParseXmlString(data);
                }
                catch(Exception ex)
                {
                    ExceptionOccurred(ex);
                    return;
                }
                
                lock (msgQueue)
                {
                    msgQueue.Enqueue(msg);
                    mreMessage.Set();
                }
            }
        } 
        public void UpdateControls(bool OnConnect)
        {
            textBoxIP.Enabled = !OnConnect;
            textBoxPort.Enabled = !OnConnect;
            textBoxClientName.Enabled = !OnConnect;
            textBoxMessage.Enabled = OnConnect;

            btnConnectServer.Enabled = !OnConnect;
            btnCloseConnection.Enabled = OnConnect;

            btnSend.Enabled = OnConnect;
            btnClear.Enabled = OnConnect;
            btnSendFile.Enabled = OnConnect;
            if (OnConnect)
            {
                status.Text = "已连接至服务器";
            }
            else
            {
                status.Text = "未连接服务";
            }
        }
        private void btnSend_Click(object sender, EventArgs e)
        {
            if (!client.Connected)
            {
                MessageBox.Show("未连接", "错误");
                return;
            }
            if (radioButtonGroup.Checked)
            {
                byte[] data = Message.GetMessageBytes(SelfInfo, new UserInformation(UserInformation.GroupID), textBoxMessage.Text);
                ThreadPool.QueueUserWorkItem(new WaitCallback(SendMessage), data);
            }
            else if (radioButtonSingle.Checked)
            {
                int id;
                try
                {
                    Int32.TryParse(listViewOnline.FocusedItem.SubItems[1].Text, out id);
                    byte[] data = Message.GetMessageBytes(SelfInfo, friends[id], textBoxMessage.Text);
                    ThreadPool.QueueUserWorkItem(new WaitCallback(SendMessage), data);
                }
                catch (Exception)
                {

                }
            }
            else
            {
                byte[] data = Message.GetMessageBytes(SelfInfo, new UserInformation(UserInformation.ServerID), textBoxMessage.Text);
                ThreadPool.QueueUserWorkItem(new WaitCallback(SendMessage), data);
            }

        }
        public void SendMessage(object state)
        {
            byte[] data = (byte[])state;
            client.Send(data, 0, data.Length, SocketFlags.None);
        }
        private void btnCloseConnection_Click(object sender, EventArgs e)
        {
            listViewOnline.Clear();
            client.Shutdown(SocketShutdown.Both);
            client.Close();
            connection.Abort();
            threadMessageProcess.Abort();
            UpdateControls(false);
        }

        private void btnSendFile_Click(object sender, EventArgs e)
        {
            if (client == null || !client.Connected)
            {
                MessageBox.Show("未连接", "错误");
                return;
            }
            if (openFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            UserInformation receiver = null;
            if (radioButtonGroup.Checked)
            {
                receiver = new UserInformation(UserInformation.GroupID);
            }
            else if (radioButtonSingle.Checked)
            {
                int id;
                try
                {
                    Int32.TryParse(listViewOnline.FocusedItem.SubItems[1].Text, out id);
                    receiver = friends[id];
                }
                catch (Exception)
                {

                }
            }
            else
            {
                receiver = new UserInformation(UserInformation.ServerID);
            }
            Thread thread = new Thread(this.SendFile);
            thread.Start(receiver);
        }
        public void SendFile(object receiver)
        {

            Socket fileSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            fileSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            UserInformation sender = new UserInformation();
            sender.Name = SelfInfo.Name;
            sender.ID = SelfInfo.ID;
            sender.IP = ((IPEndPoint)fileSocket.LocalEndPoint).Address.ToString();
            sender.Port = ((IPEndPoint)fileSocket.LocalEndPoint).Port;
            byte[] data = Message.GetFileBytes(sender, (UserInformation)receiver, new FileInformation(openFileDialog.FileName));
            ThreadPool.QueueUserWorkItem(new WaitCallback(SendMessage), data);

            new Thread(delegate() { new FileSenderForm(fileSocket, openFileDialog.FileName).ShowDialog(); }).Start();
        }
        private void Client_Load(object sender, EventArgs e)
        {
            UpdateControls(false);
        }
        private void btnClear_Click(object sender, EventArgs e)
        {
            listViewChat.Items.Clear();
        }

        public void Clean()
        {
            if (client != null)
                client.Close();
            if (connection != null)
                connection.Abort();
            if (threadMessageProcess != null)
                threadMessageProcess.Abort();
            UpdateControls(false);
        }
    }
}
