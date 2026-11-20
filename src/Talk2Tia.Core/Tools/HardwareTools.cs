using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Core.Tools;

[McpServerToolType]
public sealed class HardwareTools(TiaSession tia)
{
    [McpServerTool(Name = "hw_list_devices", ReadOnly = true, Idempotent = true)]
    [Description("Returns the hardware tree: devices, their modules (rack position, type, order number) and network interfaces.")]
    public Task<string> ListDevices(
        [Description("Only this device.")] string? device = null,
        [Description("Maximum depth of the module tree.")] int depth = 4)
        => tia.RunAsync(() =>
        {
            var devices = device is null ? Locator.AllDevices(tia.Project) : new[] { Locator.FindDevice(tia.Project, device) };
            return devices.Select(d => new
            {
                name = d.Name,
                typeIdentifier = d.TypeIdentifier,
                isGsd = d.IsGsd,
                items = d.DeviceItems.Select(i => FormatItem(i, depth - 1)).ToList(),
            }).ToList();
        });

    private static object FormatItem(DeviceItem item, int depth)
    {
        var network = item.GetService<NetworkInterface>();
        return new
        {
            name = item.Name,
            position = item.PositionNumber,
            typeIdentifier = item.TypeIdentifier,
            classification = item.Classification.ToString(),
            builtIn = item.IsBuiltIn,
            nodes = network?.Nodes.Select(n => new
            {
                name = n.Name,
                type = n.NodeType.ToString(),
                subnet = n.ConnectedSubnet?.Name,
                address = SafeAttribute(n, "Address"),
            }).ToList(),
            items = depth > 0 && item.DeviceItems.Count > 0 ? item.DeviceItems.Select(i => FormatItem(i, depth - 1)).ToList() : null,
        };
    }

    private static object? SafeAttribute(IEngineeringObject obj, string name)
    {
        try
        {
            return ValueFormatter.Format(obj.GetAttribute(name));
        }
        catch (EngineeringException)
        {
            // Not every node type exposes every attribute.
            return null;
        }
    }

    [McpServerTool(Name = "hw_get_item", ReadOnly = true, Idempotent = true)]
    [Description("Returns all attributes of a device or device item (module, interface, port). Use 'Device/Item' to disambiguate.")]
    public Task<string> GetItem([Description("Device or device item name, or 'Device/Item'.")] string name) => tia.RunAsync(() =>
    {
        IEngineeringObject obj = Locator.AllDevices(tia.Project).FirstOrDefault(d => Locator.NameEquals(d.Name, name)) as IEngineeringObject
                                 ?? Locator.FindDeviceItem(tia.Project, name);
        return new { type = obj.GetType().Name, attributes = ValueFormatter.ReadAttributes(obj) };
    });

    [McpServerTool(Name = "hw_set_attributes", Destructive = false)]
    [Description("Sets attributes of a device or device item (e.g. Name, Comment, cycle time, protection, IP parameters on a node via obj_set).")]
    public Task<string> SetAttributes(
        [Description("Device or device item name, or 'Device/Item'.")] string name,
        [Description("Attributes to set.")] Dictionary<string, JsonElement> attributes,
        [Description("Culture for multilingual texts.")] string? culture = null)
        => tia.RunAsync(() =>
        {
            IEngineeringObject obj = Locator.AllDevices(tia.Project).FirstOrDefault(d => Locator.NameEquals(d.Name, name)) as IEngineeringObject
                                     ?? Locator.FindDeviceItem(tia.Project, name);
            return new { applied = ObjectAttributes.Apply(obj, attributes, culture) };
        });

    [McpServerTool(Name = "hw_catalog_search", ReadOnly = true, Idempotent = true)]
    [Description("Searches the hardware catalog by text or order number. Returns type identifiers to use with hw_create_device / hw_plug_module.")]
    public Task<string> CatalogSearch(
        [Description("Search text, e.g. '6ES7 513-1AL02-0AB0' or 'CPU 1513'.")] string query,
        [Description("Maximum results.")] int max = 50)
        => tia.RunAsync(() =>
            tia.Portal.HardwareCatalog.Find(query).Take(max).Select(e => new
            {
                typeName = e.TypeName,
                articleNumber = e.ArticleNumber,
                version = e.Version,
                typeIdentifier = e.TypeIdentifier,
                catalogPath = e.CatalogPath,
                description = e.Description,
            }).ToList());

    [McpServerTool(Name = "hw_create_device", Destructive = false)]
    [Description("Creates a device with its main module. typeIdentifier example: 'OrderNumber:6ES7 513-1AL02-0AB0/V2.9'.")]
    public Task<string> CreateDevice(
        [Description("Type identifier from hw_catalog_search.")] string typeIdentifier,
        [Description("Name of the main item (e.g. the CPU name).")] string name,
        [Description("Device (station) name.")] string deviceName)
        => tia.RunAsync(() =>
        {
            var device = tia.Project.Devices.CreateWithItem(typeIdentifier, name, deviceName);
            return new { created = device.Name, items = Locator.AllItems(device).Select(i => i.Name).ToList() };
        });

    [McpServerTool(Name = "hw_plug_module", Destructive = false)]
    [Description("Plugs a new module into a rack or device item at a slot position.")]
    public Task<string> PlugModule(
        [Description("Parent (rack or device item) name, or 'Device/Item'.")] string parent,
        [Description("Type identifier from hw_catalog_search.")] string typeIdentifier,
        [Description("Module name.")] string name,
        [Description("Slot position number.")] int position)
        => tia.RunAsync(() =>
        {
            var p = Locator.FindDeviceItem(tia.Project, parent);
            if (!p.CanPlugNew(typeIdentifier, name, position))
            {
                var free = p.GetPlugLocations().Select(l => ValueFormatter.ReadAttributes((IEngineeringObject)l)).ToList();
                throw new McpException($"Cannot plug '{typeIdentifier}' at position {position}. Free plug locations: {Json.Serialize(free)}");
            }

            var item = p.PlugNew(typeIdentifier, name, position);
            return new { plugged = item.Name, position = item.PositionNumber };
        });

    [McpServerTool(Name = "hw_delete", Destructive = true)]
    [Description("Deletes a device or a device item (module). Requires confirm=true.")]
    public Task<string> Delete([Description("Device or 'Device/Item' name.")] string name, [Description("Explicit user approval for this destructive action.")] bool confirm = false) => tia.RunAsync(() =>
    {
        Guard.RequireConfirm(confirm, $"delete hardware '{name}'");
        var device = Locator.AllDevices(tia.Project).FirstOrDefault(d => Locator.NameEquals(d.Name, name));
        if (device is not null)
        {
            device.Delete();
        }
        else
        {
            Locator.FindDeviceItem(tia.Project, name).Delete();
        }

        return new { deleted = name };
    });

    [McpServerTool(Name = "hw_list_subnets", ReadOnly = true, Idempotent = true)]
    [Description("Lists the subnets with their connected nodes and IO systems.")]
    public Task<string> ListSubnets() => tia.RunAsync(() =>
        tia.Project.Subnets.Select(s => new
        {
            name = s.Name,
            type = s.NetType.ToString(),
            typeIdentifier = s.TypeIdentifier,
            nodes = s.Nodes.Select(n => new { name = n.Name, address = SafeAttribute(n, "Address") }).ToList(),
            ioSystems = s.IoSystems.Select(io => ValueFormatter.TryGetName(io)).ToList(),
        }).ToList());

    [McpServerTool(Name = "hw_create_subnet", Destructive = false)]
    [Description("Creates a subnet. typeIdentifier: System:Subnet.Ethernet, System:Subnet.Profibus, System:Subnet.Mpi, ...")]
    public Task<string> CreateSubnet(
        [Description("Subnet name.")] string name,
        [Description("Subnet type identifier.")] string typeIdentifier = "System:Subnet.Ethernet")
        => tia.RunAsync(() => new { created = tia.Project.Subnets.Create(typeIdentifier, name).Name });

    [McpServerTool(Name = "hw_connect_node", Destructive = false)]
    [Description("Connects a network interface node of a device item to a subnet (created if missing), optionally setting its address.")]
    public Task<string> ConnectNode(
        [Description("Interface device item, e.g. 'PLC_1/PROFINET interface_1'.")] string interfaceItem,
        [Description("Subnet name.")] string subnet,
        [Description("Node address, e.g. 192.168.0.10.")] string? address = null,
        [Description("Node index when the interface has several nodes.")] int node = 0)
        => tia.RunAsync(() =>
        {
            var item = Locator.FindDeviceItem(tia.Project, interfaceItem);
            var network = item.GetService<NetworkInterface>() ?? throw new McpException($"'{interfaceItem}' is not a network interface.");
            var n = network.Nodes.ElementAtOrDefault(node) ?? throw new McpException($"Node index {node} not found.");
            var target = tia.Project.Subnets.Find(subnet);
            if (target is null)
            {
                target = n.CreateAndConnectToSubnet(subnet);
            }
            else
            {
                n.ConnectToSubnet(target);
            }

            if (address is not null)
            {
                n.SetAttribute("Address", address);
            }

            return new { node = n.Name, subnet = target.Name, address = SafeAttribute(n, "Address") };
        });

    [McpServerTool(Name = "hw_disconnect_node", Destructive = false)]
    [Description("Disconnects a network interface node from its subnet.")]
    public Task<string> DisconnectNode(
        [Description("Interface device item.")] string interfaceItem,
        [Description("Node index.")] int node = 0)
        => tia.RunAsync(() =>
        {
            var network = Locator.FindDeviceItem(tia.Project, interfaceItem).GetService<NetworkInterface>()
                          ?? throw new McpException($"'{interfaceItem}' is not a network interface.");
            var n = network.Nodes.ElementAtOrDefault(node) ?? throw new McpException($"Node index {node} not found.");
            n.DisconnectFromSubnet();
            return new { disconnected = n.Name };
        });
}
