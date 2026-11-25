namespace NetTracker
{
    /// <summary>
    /// Неизменяемая запись о перехваченном сетевом соединении.
    /// </summary>
    /// <param name="TimeStamp">Точное время фиксации сетевого события.</param>
    /// <param name="DestIp">Удаленный IP-адрес, к которому обратился процесс.</param>
    /// <param name="DestPort">Удаленный порт подключения.</param>
    /// <param name="Domain">DNS-имя (домен), соответствующее IP-адресу, либо пометка о неизвестном домене.</param>
    public readonly record struct NetworkRecord(DateTime TimeStamp, string DestIp, int DestPort, string Domain);
}