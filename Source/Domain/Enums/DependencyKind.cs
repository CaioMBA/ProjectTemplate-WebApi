namespace Domain.Enums;

public enum DependencyKind
{
    Self,
    Database,
    DocumentDatabase,
    Broker,
    Cache,
    Api,
    Scheduler,
}