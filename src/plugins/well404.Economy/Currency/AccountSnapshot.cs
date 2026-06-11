namespace well404.Economy.Currency
{
    /// <summary>A read-only view of one account's balance, for listing/administration.</summary>
    public sealed class AccountSnapshot
    {
        public AccountSnapshot(string ownerType, string ownerId, decimal balance)
        {
            OwnerType = ownerType;
            OwnerId = ownerId;
            Balance = balance;
        }

        public string OwnerType { get; }

        public string OwnerId { get; }

        public decimal Balance { get; }
    }
}
