#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.CoreBase;
using FTOptix.Core;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Collections.Generic;
using System.Linq;
#endregion

public class PingCommandLogic : BaseNetLogic
{
    public override void Start()
    {

    }

    public override void Stop()
    {
        CommonLogic.DisposeTask(PingTask);
    }

    [ExportMethod]
    public void RequestPing(string ipAddress)
    {
        if (!IsPingRunning)
        {
            PingTask = new LongRunningTask(PingTest, ipAddress, LogicObject);
            PingTask.Start();
        }
    }

    private void PingTest(LongRunningTask task, object parameters)
    {
        try
        {
            LogicObject.GetVariable("PingResult").Value = string.Empty;
            IsPingRunning = true;
            if (parameters is not string ipAddress)
            {
                throw new ArgumentException("Invalid parameters for Ping method. Expected a single string parameter for IP address.");
            }

            int attempts = 4;
            int timeout = 1000;
            var buffer = new byte[32];
            var resultToPrint = new StringBuilder();

            int sent = 0;
            int received = 0;
            int lost = 0;
            var roundTripTimes = new List<long>();

            resultToPrint.AppendLine($"Pinging {ipAddress} with {buffer.Length} bytes of data:");
            LogicObject.GetVariable("PingResult").Value = resultToPrint.ToString();
            using Ping pingSender = new();
            for (int i = 0; i < attempts; i++)
            {
                if (task.IsCancellationRequested)
                {
                    break;
                }
                sent++;
                try
                {
                    PingReply reply = pingSender.Send(ipAddress, timeout, buffer);
                    if (reply.Status == IPStatus.Success)
                    {
                        received++;
                        roundTripTimes.Add(reply.RoundtripTime);
                        resultToPrint.AppendLine($"Reply from {reply.Address}: bytes={reply.Buffer.Length} time={reply.RoundtripTime}ms");
                        LogicObject.GetVariable("PingResult").Value = resultToPrint.ToString();
                    }
                    else
                    {
                        lost++;
                        resultToPrint.AppendLine($"Attempt {i + 1}: No reply ({reply.Status})");
                        LogicObject.GetVariable("PingResult").Value = resultToPrint.ToString();
                    }
                }
                catch (Exception ex)
                {
                    lost++;
                    resultToPrint.AppendLine($"Attempt {i + 1}: Ping error: {ex.Message}");
                    LogicObject.GetVariable("PingResult").Value = resultToPrint.ToString();
                }
            }

            int lossPercent = sent > 0 ? (lost * 100 / sent) : 0;
            resultToPrint.AppendLine();
            resultToPrint.AppendLine($"Ping statistics for {ipAddress}:");
            resultToPrint.AppendLine($"    Packets: Sent = {sent}, Received = {received}, Lost = {lost} ({lossPercent}% loss)");

            if (roundTripTimes.Count > 0)
            {
                long min = roundTripTimes.Min();
                long max = roundTripTimes.Max();
                long avg = (long)roundTripTimes.Average();
                resultToPrint.AppendLine("Approximate round trip times in milli-seconds:");
                resultToPrint.AppendLine($"    Minimum = {min}ms, Maximum = {max}ms, Average = {avg}ms");
            }
            LogicObject.GetVariable("PingResult").Value = resultToPrint.ToString();
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, $"Error during ping operation: {ex.Message}");
        }
        finally
        {
            IsPingRunning = false;
        }
    }

    private LongRunningTask PingTask;
    private bool _isPingRunning = false;
    public bool IsPingRunning
    {
        get => _isPingRunning;
        set
        {
            if (_isPingRunning != value)
            {
                _isPingRunning = value;
                LogicObject.GetVariable("IsRunning").Value = value;
            }
        }
    }

}
