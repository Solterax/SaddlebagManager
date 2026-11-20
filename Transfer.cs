using System.Diagnostics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace SaddlebagManager;

internal sealed unsafe class Transfer(IGameGui gameGui, IClientState clientState, IDataManager data)
{
    private static readonly InventoryType[] InventoryPages =
        [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    private static readonly InventoryType[] StandardPages = [InventoryType.SaddleBag1, InventoryType.SaddleBag2];
    private static readonly InventoryType[] PremiumPages =
        [InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2];
    private static readonly TimeSpan MoveInterval = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromSeconds(2);

    private readonly Queue<Slot> sourceSlots = new();
    private InventoryType[] destinations = [];
    private Move? pending;
    private StartRequest? startRequest;
    private bool stopRequested;
    private int moved;
    private int remaining;

    public bool SaddlebagReady => GetVisibleSaddlebag() != null;
    public bool IsRunning { get; private set; }
    public bool IsBusy => IsRunning || startRequest.HasValue;
    public string Status { get; private set; } = "";

    public void Start(bool deposit, bool includePremium)
    {
        if (!IsBusy)
            startRequest = new(deposit, includePremium);
    }

    public void RequestStop() => stopRequested = true;

    internal AtkUnitBase* GetVisibleSaddlebag()
    {
        if (!clientState.IsLoggedIn)
            return null;
        var addon = GetVisibleAddon("InventoryBuddy");
        return addon != null ? addon : GetVisibleAddon("InventoryBuddy2");
    }

    private AtkUnitBase* GetVisibleAddon(string name)
    {
        var addon = gameGui.GetAddonByName<AtkUnitBase>(name);
        return addon != null && addon->IsVisible && addon->IsReady ? addon : null;
    }

    public void Update()
    {
        if (startRequest is { } request)
        {
            startRequest = null;
            Initialize(request);
        }
        if (!IsRunning)
            return;
        if (!SaddlebagReady)
        {
            Finish("Saddlebag closed.");
            return;
        }

        if (pending is { } move)
        {
            if (Stopwatch.GetElapsedTime(move.Started) < MoveInterval)
                return;
            if (!ConfirmMove(move))
                return;
            pending = null;
        }

        if (stopRequested)
        {
            Finish("Transfer stopped.");
            return;
        }

        while (sourceSlots.TryPeek(out var source))
        {
            if (!TryRead(source, out var stack))
            {
                Finish("A source container became unavailable.");
                return;
            }
            if (stack.IsEmpty)
            {
                sourceSlots.Dequeue();
                continue;
            }

            var itemSheet = data.GetExcelSheet<Item>();
            var stackLimit = itemSheet.TryGetRow(stack.ItemId, out var item) ? (int)item.StackSize : 1;
            if (!FindDestination(stack, stackLimit, out var target, out var targetStack))
            {
                SkipSource();
                continue;
            }

            var expectedQuantity = targetStack.IsEmpty ? stack.Quantity : Math.Min(stack.Quantity, stackLimit - targetStack.Quantity);
            pending = new Move(source, stack, target, targetStack, expectedQuantity, Stopwatch.GetTimestamp());
            InventoryManager.Instance()->MoveItemSlot(source.Container, source.Index, target.Container, target.Index, true);
            return;
        }

        Finish($"Transfer finished. {remaining} occupied source slot(s) remain.");
    }

    private void Initialize(StartRequest request)
    {
        if (!SaddlebagReady)
        {
            Status = "Open your Chocobo Saddlebag first.";
            return;
        }
        if (request.IncludePremium && !PremiumPages.All(IsLoaded))
        {
            Status = "Premium saddlebag unavailable. Clear the premium option or open the premium tab and try again.";
            return;
        }
        if (!StandardPages.All(IsLoaded) || !InventoryPages.All(IsLoaded))
        {
            Status = "Inventory is still loading. Open your inventory and saddlebag, then try again.";
            return;
        }

        var saddlebag = request.IncludePremium ? [.. StandardPages, .. PremiumPages] : StandardPages;
        var sources = request.Deposit ? InventoryPages : saddlebag;
        destinations = request.Deposit ? saddlebag : InventoryPages;
        sourceSlots.Clear();
        foreach (var type in sources)
        {
            var container = GetContainer(type);
            for (ushort index = 0; index < container->Size; index++)
            {
                if (container->GetInventorySlot(index)->ItemId != 0)
                    sourceSlots.Enqueue(new Slot(type, index));
            }
        }

        moved = remaining = 0;
        stopRequested = false;
        pending = null;
        IsRunning = true;
        Status = "Transferring items…";
    }

    private bool FindDestination(Stack stack, int limit, out Slot target, out Stack targetStack)
    {
        Slot? empty = null;
        Slot? partial = null;
        Stack partialStack = default;
        var partialSpace = 0;

        foreach (var type in destinations)
        {
            var container = GetContainer(type);
            if (container == null)
                continue;
            for (ushort index = 0; index < container->Size; index++)
            {
                var slot = new Slot(type, index);
                var candidate = Read(container->GetInventorySlot(index));
                if (candidate.IsEmpty)
                {
                    empty ??= slot;
                    continue;
                }
                if (limit <= 1 || stack.Collectable || !candidate.Matches(stack))
                    continue;

                var space = limit - candidate.Quantity;
                if (space >= stack.Quantity)
                {
                    target = slot;
                    targetStack = candidate;
                    return true;
                }
                if (space > partialSpace)
                {
                    partial = slot;
                    partialStack = candidate;
                    partialSpace = space;
                }
            }
        }

        target = empty ?? partial ?? default;
        targetStack = empty.HasValue ? default : partialStack;
        return empty.HasValue || partial.HasValue;
    }

    private bool ConfirmMove(Move move)
    {
        if (!TryRead(move.Source, out var source) || !TryRead(move.Target, out var target))
        {
            Finish("A container became unavailable during a transfer.");
            return false;
        }

        var quantity = move.SourceBefore.Quantity - source.Quantity;
        if (quantity == move.ExpectedQuantity && (source.IsEmpty || source.Matches(move.SourceBefore)) &&
            target.Matches(move.SourceBefore) && target.Quantity == move.TargetBefore.Quantity + quantity)
        {
            moved += quantity;
            if (source.IsEmpty)
                sourceSlots.Dequeue();
            Status = $"Moved {moved} item(s). {sourceSlots.Count} source slot(s) left to check.";
            return true;
        }
        if (Stopwatch.GetElapsedTime(move.Started) < ConfirmationTimeout)
            return false;
        if (source != move.SourceBefore || target != move.TargetBefore)
        {
            Finish("A slot changed unexpectedly. Check your bags before retrying.");
            return false;
        }

        // A rejected move leaves the original stack in place.
        SkipSource();
        return true;
    }

    private void SkipSource()
    {
        sourceSlots.Dequeue();
        remaining++;
    }

    private void Finish(string message)
    {
        pending = null;
        sourceSlots.Clear();
        IsRunning = false;
        Status = $"{message} Moved {moved} item(s).";
    }

    private static bool IsLoaded(InventoryType type) => GetContainer(type) != null;

    private static InventoryContainer* GetContainer(InventoryType type)
    {
        var manager = InventoryManager.Instance();
        if (manager == null)
            return null;
        var container = manager->GetInventoryContainer(type);
        return container != null && container->IsLoaded && container->Size > 0 ? container : null;
    }

    private static bool TryRead(Slot slot, out Stack stack)
    {
        var container = GetContainer(slot.Container);
        var item = container != null && slot.Index < container->Size ? container->GetInventorySlot(slot.Index) : null;
        stack = Read(item);
        return item != null;
    }

    private static Stack Read(InventoryItem* item) =>
        item == null || item->ItemId == 0
            ? default
            : new Stack(item->GetBaseItemId(), item->Quantity, item->Flags, item->IsCollectable());

    private readonly record struct StartRequest(bool Deposit, bool IncludePremium);
    private readonly record struct Slot(InventoryType Container, ushort Index);
    private readonly record struct Stack(uint ItemId, int Quantity, InventoryItem.ItemFlags Flags, bool Collectable)
    {
        public bool IsEmpty => ItemId == 0;
        public bool Matches(Stack other) => ItemId == other.ItemId && Flags == other.Flags &&
            Collectable == other.Collectable;
    }
    private readonly record struct Move(
        Slot Source, Stack SourceBefore, Slot Target, Stack TargetBefore, int ExpectedQuantity, long Started);
}
