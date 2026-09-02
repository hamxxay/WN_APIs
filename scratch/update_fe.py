import os

ts_path = r"F:\WorkNest_FE\src\app\pages\admin\manage\manage.ts"
html_path = r"F:\WorkNest_FE\src\app\pages\admin\manage\manage.html"

# Update manage.ts
with open(ts_path, 'r', encoding='utf-8') as f:
    ts_code = f.read()

# 1. Update bookingFirstInvoiceTotal
old_booking_total = """get bookingFirstInvoiceTotal(): number {
    const subtotal = this.bookingBillingAmount + this.bookingTaxAmount + this.effectiveSecurityDeposit;
    return parseFloat(Math.max(0, subtotal - this.bookingDiscountAmount).toFixed(2));
  }"""

new_booking_total = """get bookingFirstInvoiceTotal(): number {
    const subtotal = this.bookingBillingAmount + this.bookingTaxAmount + this.effectiveSecurityDeposit;
    return parseFloat(Math.max(0, subtotal - this.bookingFirstInvoiceDiscount).toFixed(2));
  }"""

ts_code = ts_code.replace(old_booking_total, new_booking_total)

# 2. Update quotationFirstInvoiceTotal to include quotationFirstInvoiceDiscount
old_quotation_total = """get quotationFirstInvoiceTotal(): number {
    const subtotal = this.quotationBillingAmount + this.quotationTaxAmount + this.effectiveQuotationSecurityDeposit;
    return parseFloat(Math.max(0, subtotal - this.quotationDiscountAmount).toFixed(2));
  }"""

new_quotation_total = """get quotationFirstInvoiceDiscount(): number {
    if (this.isQuotationMeetingRoom) return this.quotationDiscountAmount;
    const contractM = Math.max(1, Number(this.quotationMonths || 12));
    const billingM = Math.max(1, Number(this.quotationBillingPeriodMonths || 3));
    if (this.quotationDiscountType === 'Percentage') {
      return parseFloat(((this.quotationBillingAmount * Number(this.quotationDiscountPercentage || 0)) / 100).toFixed(2));
    }
    if (this.quotationDiscountAmount > this.quotationBillingAmount && contractM > billingM) {
      return parseFloat(((this.quotationDiscountAmount * billingM) / contractM).toFixed(2));
    }
    return this.quotationDiscountAmount;
  }

  get quotationFirstInvoiceTotal(): number {
    const subtotal = this.quotationBillingAmount + this.quotationTaxAmount + this.effectiveQuotationSecurityDeposit;
    return parseFloat(Math.max(0, subtotal - this.quotationFirstInvoiceDiscount).toFixed(2));
  }"""

ts_code = ts_code.replace(old_quotation_total, new_quotation_total)

# 3. Update submitAdminBooking discount assignment
old_discount_assign = "const discount = this.bookingDiscountAmount;"
new_discount_assign = "const discount = this.bookingFirstInvoiceDiscount;"

ts_code = ts_code.replace(old_discount_assign, new_discount_assign)

with open(ts_path, 'w', encoding='utf-8') as f:
    f.write(ts_code)

print("manage.ts updated successfully.")

# Update manage.html
with open(html_path, 'r', encoding='utf-8') as f:
    html_code = f.read()

html_code = html_code.replace(
    "@if (bookingDiscountAmount > 0) {\n          <div style=\"display:flex;justify-content:space-between;font-size:.82rem;margin-bottom:3px\">\n            <span style=\"color:#ef4444\">Discount:</span>\n            <strong style=\"color:#ef4444\">PKR {{ bookingDiscountAmount | number:'1.2-2' }}</strong>",
    "@if (bookingFirstInvoiceDiscount > 0) {\n          <div style=\"display:flex;justify-content:space-between;font-size:.82rem;margin-bottom:3px\">\n            <span style=\"color:#ef4444\">Discount:</span>\n            <strong style=\"color:#ef4444\">PKR {{ bookingFirstInvoiceDiscount | number:'1.2-2' }}</strong>"
)

html_code = html_code.replace(
    "@if (quotationDiscountAmount > 0) {\n          <div style=\"display:flex;justify-content:space-between;font-size:.82rem;margin-bottom:3px\">\n            <span style=\"color:#ef4444\">Discount:</span>\n            <strong style=\"color:#ef4444\">PKR {{ quotationDiscountAmount | number:'1.2-2' }}</strong>",
    "@if (quotationFirstInvoiceDiscount > 0) {\n          <div style=\"display:flex;justify-content:space-between;font-size:.82rem;margin-bottom:3px\">\n            <span style=\"color:#ef4444\">Discount:</span>\n            <strong style=\"color:#ef4444\">PKR {{ quotationFirstInvoiceDiscount | number:'1.2-2' }}</strong>"
)

with open(html_path, 'w', encoding='utf-8') as f:
    f.write(html_code)

print("manage.html updated successfully.")
