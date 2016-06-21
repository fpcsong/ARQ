using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
namespace ARQ
{
    class Program
    {
        /// <summary>
        /// 全局变量说明：
        /// sf 窗口起始位置，sn下一个即将发送的消息，sw窗口大小
        /// dataLength这里固定了16
        /// stor rtos 是两个线程安全的队列，这里作为信道使用
        /// 使用互斥对象来保证打印函数对控制台的独立占用
        /// 所有的算数运算都需要使用原子操作来保证线程同步
        /// </summary>
        static int sf, sn, rn, ss, sw, dataLength;
        static ConcurrentQueue<int> stor = new ConcurrentQueue<int>();
        static ConcurrentQueue<int> rtos = new ConcurrentQueue<int>();
        static string MutexName = "fpcsong";
        static Mutex mutex = new Mutex(false, MutexName);
        static Random rand = new Random();
        static void Main(string[] args)
        {
            Console.SetWindowSize(160, 50);
            sf = sn = rn = ss = 0;
            dataLength = 16;
            Console.WriteLine("Please input the window size of protocol and then maxmize the window to see the result:");
            //读取发送窗口的大小并做好限制，直到合法的被读入为止
            while ((false == int.TryParse(Console.ReadLine(), out sw) || (sw > 15 || sw < 1))) { };
            //使用三个线程来模拟发送消息，接受消息和接受ack三种动作
            new Thread(() => send()).Start();
            new Thread(() => recive()).Start();
            new Thread(() => recACK()).Start();
        }
        /// <summary>
        /// 根据当前窗口的位置和相应的动作打印状态
        /// </summary>
        /// <param name="sf"></param>
        /// <param name="sn"></param>
        /// <param name="rn"></param>
        /// <param name="para"></param>
        static void draw(int sf, int sn, int rn, string para)
        {
            //保证一个时刻只有一个draw函数在运行
            mutex.WaitOne();
            if (sf == 16)
            {
                for (int i = 0; i < 16; i++) sended(i);
                Console.Write("{0,25}      ", para);
                for (int i = 0; i < 16; i++) sended(i);
                Console.WriteLine();
                mutex.ReleaseMutex();
                return;
            }
            for (int i = 0; i < sf; i++) sended(i);
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.Write("[ ");
            for (int i = sf; i < sn; i++) waitForResponse(i);
            for (int i = sn; i < Math.Min(sf + sw, dataLength); i++) dataInWindow(i);
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.Write(" ]");
            for (int i = sf + sw; i < dataLength; i++) unsend(i);
            Console.ResetColor();
            Console.Write("{0,25}      ", para);
            for (int i = 0; i < rn; i++) sended(i);
            if (rn < 16) waitForResponse(rn);
            for (int i = rn + 1; i < dataLength; i++) unsend(i);
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();
            mutex.ReleaseMutex();
        }
        /// <summary>
        /// 打印还没发送的数据
        /// </summary>
        /// <param name="i"></param>
        static void unsend(int i)
        {
            Console.BackgroundColor = ConsoleColor.DarkGreen;
            Console.Write(i.ToString() + ' ');
        }
        /// <summary>
        /// 打印已经发送的数据
        /// </summary>
        /// <param name="i"></param>
        static void sended(int i)
        {
            Console.BackgroundColor = ConsoleColor.Black;
            Console.Write(i.ToString() + ' ');
        }
        /// <summary>
        /// 打印发送了但是还没接受到ack的数据
        /// </summary>
        /// <param name="i"></param>
        static void waitForResponse(int i)
        {
            Console.BackgroundColor = ConsoleColor.DarkRed;
            Console.Write(i.ToString() + ' ');
        }
        /// <summary>
        /// 打印在窗口中但是还没有被发送的数据
        /// </summary>
        /// <param name="i"></param>
        static void dataInWindow(int i)
        {
            Console.BackgroundColor = ConsoleColor.Green;
            Console.Write(i.ToString() + ' ');
        }
        /// <summary>
        /// 发送数据的线程
        /// </summary>
        static void send()
        {
            
            while (true)
            {
                //如果当前数据不在窗口中就阻塞进程 ss是为了在回退的时候不改变sn的位置
                while (ss - sf >= sw) { };
                //发送消息
                stor.Enqueue(ss);
                //发送标记位置移动到下一位
                Interlocked.Increment(ref ss);
                if (ss > sn)
                {
                    Interlocked.Add(ref sn, ss - sn);
                }
                draw(sf, sn, rn, "Send msg " + (ss - 1).ToString());
                if (sn == 16)
                {
                    //发送完毕
                    draw(sf, sn, rn, "Send completed");
                }
            }
        }
        /// <summary>
        /// 从stor中接受ack
        /// 约定ack==110就会被发送端当成timeout来处理，因为不管消息丢失和ack丢失，最后的结果
        /// 都是发送端timeout，所以可以通过模拟timeout来模拟数据包的丢失和出错
        /// 这里程序有个问题，就是队列保证了消息的时序，但是实际上消息在网络传输中可能会失去时序
        /// 这里考虑的不够
        /// </summary>
        static void recACK()
        {
            while (true)
            {
                while (rtos.IsEmpty) { };
                int ack = 0;
                while (false == rtos.TryDequeue(out ack)) { };
                if (ack == 110)
                {
                    //处理超时，将消息在sf和sn中间的重新发送
                    Interlocked.Add(ref ss, sf - ss);
                    draw(sf, sn, rn, "ack" + (sf + 1).ToString() + " missed,timeout");
                }
                else
                {
                    //重发之后收到ack可能含有小于sf的了，在这里丢掉
                    //因为有一个原则收到大号ack可以表示之前的消息都被收到了
                    if (ack <= sn && ack > sf) sf = ack; else continue;
                    if (ack == 16)
                    {
                        //当接收完所有的ack就可以终止这个演示程序了
                        sf = 16;
                        draw(sf, sn, rn, "All acks received");
                        Environment.Exit(0);
                        return;
                    }
                    draw(sf, sn, rn, "Receive ack " + ack.ToString());
                }
            }
        }
        /// <summary>
        /// 从stor中读取消息，并返回对应的ack或者110(timeout) 
        /// 可以在这里设置丢失的概率来达到一个好的演示效果
        /// </summary>
        static void recive()
        {
            while (true)
            {
                while (stor.IsEmpty) { };
                int msg = 0;
                while (false == stor.TryDequeue(out msg)) { };
                if (msg == rn)
                {
                    Interlocked.Increment(ref rn);
                    if (rand.Next(0, 10) >= 2) rtos.Enqueue(rn);
                    else rtos.Enqueue(110);
                    if (rn == 16)
                    {
                        draw(sf, sn, rn, "Receive completed");
                    }
                    else
                    {
                        draw(sf, sn, rn, "Receive msg " + msg.ToString());
                    }
                }
                else
                {
                    if (rand.Next(0, 10) >= 2) rtos.Enqueue(rn);
                    else rtos.Enqueue(110);
                }
            }
        }
    }
}
/*样例输出 sw = 4
Please input the window size of protocol and then maxmize the window to see the result:
4
[ 0 1 2 3  ]4 5 6 7 8 9 10 11 12 13 14 15                Send msg 0      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 [ 1 2 3 4  ]5 6 7 8 9 10 11 12 13 14 15             Receive ack 1      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



[ 0 1 2 3  ]4 5 6 7 8 9 10 11 12 13 14 15             Receive msg 0      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 [ 1 2 3 4  ]5 6 7 8 9 10 11 12 13 14 15                Send msg 1      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 [ 1 2 3 4  ]5 6 7 8 9 10 11 12 13 14 15             Receive msg 1      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 [ 2 3 4 5  ]6 7 8 9 10 11 12 13 14 15             Receive ack 2      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 [ 2 3 4 5  ]6 7 8 9 10 11 12 13 14 15                Send msg 2      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 [ 2 3 4 5  ]6 7 8 9 10 11 12 13 14 15             Receive msg 2      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 [ 3 4 5 6  ]7 8 9 10 11 12 13 14 15             Receive ack 3      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 [ 3 4 5 6  ]7 8 9 10 11 12 13 14 15                Send msg 3      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 [ 3 4 5 6  ]7 8 9 10 11 12 13 14 15             Receive msg 3      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 [ 4 5 6 7  ]8 9 10 11 12 13 14 15             Receive ack 4      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 [ 4 5 6 7  ]8 9 10 11 12 13 14 15                Send msg 4      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 [ 4 5 6 7  ]8 9 10 11 12 13 14 15             Receive msg 4      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 [ 5 6 7 8  ]9 10 11 12 13 14 15             Receive ack 5      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 [ 5 6 7 8  ]9 10 11 12 13 14 15                Send msg 5      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 [ 5 6 7 8  ]9 10 11 12 13 14 15             Receive msg 5      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 [ 6 7 8 9  ]10 11 12 13 14 15             Receive ack 6      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 [ 6 7 8 9  ]10 11 12 13 14 15                Send msg 6      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 [ 6 7 8 9  ]10 11 12 13 14 15             Receive msg 6      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 [ 7 8 9 10  ]11 12 13 14 15             Receive ack 7      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 [ 7 8 9 10  ]11 12 13 14 15                Send msg 7      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 [ 7 8 9 10  ]11 12 13 14 15             Receive msg 7      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 [ 8 9 10 11  ]12 13 14 15             Receive ack 8      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 [ 8 9 10 11  ]12 13 14 15                Send msg 8      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 [ 8 9 10 11  ]12 13 14 15             Receive msg 8      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 [ 9 10 11 12  ]13 14 15             Receive ack 9      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 [ 9 10 11 12  ]13 14 15                Send msg 9      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 [ 9 10 11 12  ]13 14 15             Receive msg 9      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 [ 10 11 12 13  ]14 15            Receive ack 10      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 [ 10 11 12 13  ]14 15               Send msg 10      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 [ 10 11 12 13  ]14 15            Receive msg 10      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 10 [ 11 12 13 14  ]15            Receive ack 11      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 10 [ 11 12 13 14  ]15               Send msg 11      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 10 [ 11 12 13 14  ]15            Receive msg 11      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 10 11 [ 12 13 14 15  ]           Receive ack 12      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 10 11 [ 12 13 14 15  ]              Send msg 12      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 10 11 [ 12 13 14 15  ]           Receive msg 12      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 10 11 12 [ 13 14 15  ]           Receive ack 13      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 10 11 12 [ 13 14 15  ]              Send msg 13      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 10 11 12 [ 13 14 15  ]           Receive msg 13      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 10 11 12 13 [ 14 15  ]           Receive ack 14      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 10 11 12 13 [ 14 15  ]              Send msg 14      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 10 11 12 13 [ 14 15  ]           Receive msg 14      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 [ 15  ]           Receive ack 15      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 [ 15  ]              Send msg 15      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 [ 15  ]        Receive completed      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15



0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15         All acks received      0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15
Press any key to continue . . .
*/
