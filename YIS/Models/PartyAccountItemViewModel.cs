namespace YIS.Models
{
    public class PartyAccountTagItem
    {
        public long AccountId { get; set; }
        public string AccountName { get; set; } = string.Empty;
    }

    public class SupplierAccountMapping
    {
        public long? PayableAccountId { get; set; }
        public long? PurchaseAccountId { get; set; }
        public long? TaxAccountId { get; set; }
        public long? CommissionAccountId { get; set; }
    }

    public class CustomerAccountMapping
    {
        public long? ReceivableAccountId { get; set; }
        public long? SalesAccountId { get; set; }
        public long? TaxAccountId { get; set; }
        public long? CommissionAccountId { get; set; }
    }
}
