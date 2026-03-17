using DemoProjekat.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DemoProjekat.Tools
{
    [McpServerToolType]
    public class PaymentDetailTools
    {
        private readonly PaymentDetailContext _db;

        public PaymentDetailTools(PaymentDetailContext db)
        {
            _db = db;
        }

        [McpServerTool, Description("Vraca sve payment detail zapise iz baze.")]
        public async Task<string> ListPaymentDetails()
        {
            var items = await _db.PaymentDetails
                .OrderByDescending(x => x.PaymentDetailId)
                .ToListAsync();

            if (!items.Any())
                return "Nema payment detail zapisa.";

            return string.Join("\n", items.Select(x =>
                $"ID={x.PaymentDetailId}, Owner={x.CardOwnerName}, Number={x.CardNumber}, Exp={x.ExpirationDate}, CVV={x.SecurityCode}"));
        }

        [McpServerTool, Description("Dodaje novi payment detail zapis.")]
        public async Task<string> AddPaymentDetail(
            [Description("Ime vlasnika kartice")] string cardOwnerName,
            [Description("Broj kartice")] string cardNumber,
            [Description("Datum isteka, npr 12/27")] string expirationDate,
            [Description("Sigurnosni kod")] string securityCode)
        {
            var item = new PaymentDetail
            {
                CardOwnerName = cardOwnerName,
                CardNumber = cardNumber,
                ExpirationDate = expirationDate,
                SecurityCode = securityCode
            };

            _db.PaymentDetails.Add(item);
            await _db.SaveChangesAsync();

            return $"Uspesno dodat zapis sa ID={item.PaymentDetailId}.";
        }

        [McpServerTool, Description("Brise payment detail zapis po ID-u.")]
        public async Task<string> DeletePaymentDetail(
            [Description("ID zapisa")] int id)
        {
            var item = await _db.PaymentDetails.FindAsync(id);

            if (item == null)
                return $"Zapis sa ID={id} ne postoji.";

            _db.PaymentDetails.Remove(item);
            await _db.SaveChangesAsync();

            return $"Obrisan zapis sa ID={id}.";
        }
    }
}
