using JevGen;

[assembly: JevJsonContext(typeof(TicketRouting.TicketJsonContext))]

Console.WriteLine(JevGenDebug.Describe<TicketRouting.ITicketAI>());
