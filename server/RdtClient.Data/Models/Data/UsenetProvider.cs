using System.ComponentModel.DataAnnotations;

namespace RdtClient.Data.Models.Data;

public class UsenetProvider
{
    [Key]
    public Guid UsenetProviderId { get; set; }

    public String Host { get; set; } = null!;

    public Int32 Port { get; set; }

    public Boolean UseSsl { get; set; }

    public String? Username { get; set; }

    public String? Password { get; set; }

    public Int32 MaxConnections { get; set; }

    public Int32 Priority { get; set; }

    public Boolean Enabled { get; set; }
}
