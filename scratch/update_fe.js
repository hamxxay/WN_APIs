const fs = require('fs');
const path = require('path');

const feBase = 'F:\\WorkNest_FE';

// 1. Update admin.service.ts
const adminServicePath = path.join(feBase, 'src\\app\\services\\admin.service.ts');
let adminServiceContent = fs.readFileSync(adminServicePath, 'utf8');

if (!adminServiceContent.includes('getCustomerActiveSpaces')) {
    const methods = `
  // ============= ATTENDANTS & ACCESS CONTROL =============
  addAttendant(data: any): Observable<any> {
    return this.http.post<any>(\`\${this.api}/attendants\`, data);
  }
  updateAttendant(personId: number, data: any): Observable<any> {
    return this.http.put<any>(\`\${this.api}/attendants/\${personId}\`, data);
  }
  getCustomerAttendants(customerId: number): Observable<any[]> {
    return this.http.get<any[]>(\`\${this.api}/customers/\${customerId}/attendants\`);
  }
  getCustomerActiveSpaces(customerId: number): Observable<any[]> {
    return this.http.get<any[]>(\`\${this.api}/customers/\${customerId}/active-spaces\`);
  }
  getBookingAttendants(bookingDetailId: number): Observable<any[]> {
    return this.http.get<any[]>(\`\${this.api}/bookings/\${bookingDetailId}/attendants\`);
  }
  checkAttendantCapacity(bookingDetailId: number): Observable<any> {
    return this.http.get<any>(\`\${this.api}/bookings/\${bookingDetailId}/capacity-check\`);
  }
  assignAttendantToBooking(bookingDetailId: number, data: any): Observable<any> {
    return this.http.post<any>(\`\${this.api}/bookings/\${bookingDetailId}/attendants\`, data);
  }
  removeAttendantFromBooking(bookingDetailId: number, personId: number): Observable<any> {
    return this.http.delete<any>(\`\${this.api}/bookings/\${bookingDetailId}/attendants/\${personId}\`);
  }
  toggleAccessStatus(data: any): Observable<any> {
    return this.http.patch<any>(\`\${this.api}/access-status\`, data);
  }
  getHikvisionExport(): Observable<any[]> {
    return this.http.get<any[]>(\`\${this.api}/access-status/export\`);
  }
}
`;
    adminServiceContent = adminServiceContent.replace(/}\s*$/, methods);
    fs.writeFileSync(adminServicePath, adminServiceContent, 'utf8');
    console.log('Updated admin.service.ts');
}

// 2. Create attendant-management component directory & files
const compDir = path.join(feBase, 'src\\app\\pages\\admin\\attendant-management');
if (!fs.existsSync(compDir)) {
    fs.mkdirSync(compDir, { recursive: true });
}

// attendant-management.ts
const tsContent = `import { Component, signal, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminService } from '../../../services/admin.service';

@Component({
  selector: 'app-attendant-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './attendant-management.html',
  styleUrl: './attendant-management.css'
})
export class AttendantManagement implements OnInit {
  private admin = inject(AdminService);

  customers = signal<any[]>([]);
  selectedCustomerId = '';
  
  activeSpaces = signal<any[]>([]);
  selectedBookingDetailId: number | null = null;
  selectedSpace: any = null;

  capacityInfo = signal<any>(null);
  attendants = signal<any[]>([]);
  loadingAttendants = false;

  // Add Attendant Modal
  showAddModal = false;
  addMode: 'new' | 'existing' = 'new';
  existingCompanyAttendants = signal<any[]>([]);
  selectedExistingPersonId: number | null = null;

  // Form Fields
  newName = '';
  newEmail = '';
  newPhone = '';
  newIdType = 'CNIC';
  newIdNumber = '';

  // Warning Modal
  showWarningModal = false;
  pendingAssignmentPayload: any = null;
  warningModalText = '';
  estimatedSurcharge = 0;
  seatPrice = 0;

  // Export Modal
  showExportModal = false;
  exportDataJson = '';

  ngOnInit() {
    this.loadCustomers();
  }

  loadCustomers() {
    this.admin.getCustomers(1, 1000, '').subscribe({
      next: (res: any) => {
        const raw = res?.data ?? res?.rows ?? res?.items ?? (Array.isArray(res) ? res : []);
        this.customers.set(raw);
      },
      error: (err) => console.error('Failed to load customers', err)
    });
  }

  onCustomerChange() {
    this.selectedBookingDetailId = null;
    this.selectedSpace = null;
    this.capacityInfo.set(null);
    this.attendants.set([]);

    if (!this.selectedCustomerId) {
      this.activeSpaces.set([]);
      return;
    }

    const cid = parseInt(this.selectedCustomerId, 10);
    this.admin.getCustomerActiveSpaces(cid).subscribe({
      next: (res: any) => {
        this.activeSpaces.set(res || []);
      },
      error: (err) => alert('Failed to load active spaces for customer: ' + (err.error?.message || err.message))
    });

    this.admin.getCustomerAttendants(cid).subscribe({
      next: (res: any) => {
        this.existingCompanyAttendants.set(res || []);
      }
    });
  }

  selectSpace(space: any) {
    this.selectedBookingDetailId = space.bookingDetailId;
    this.selectedSpace = space;
    this.loadAttendantsForSpace();
  }

  loadAttendantsForSpace() {
    if (!this.selectedBookingDetailId) return;
    this.loadingAttendants = true;

    this.admin.checkAttendantCapacity(this.selectedBookingDetailId).subscribe({
      next: (cap: any) => this.capacityInfo.set(cap)
    });

    this.admin.getBookingAttendants(this.selectedBookingDetailId).subscribe({
      next: (res: any) => {
        this.attendants.set(res || []);
        this.loadingAttendants = false;
      },
      error: () => this.loadingAttendants = false
    });
  }

  openAddModal() {
    this.addMode = 'new';
    this.newName = '';
    this.newEmail = '';
    this.newPhone = '';
    this.newIdType = 'CNIC';
    this.newIdNumber = '';
    this.selectedExistingPersonId = null;
    this.showAddModal = true;
  }

  closeAddModal() {
    this.showAddModal = false;
  }

  submitAddAttendant() {
    if (!this.selectedCustomerId || !this.selectedBookingDetailId) return;

    if (this.addMode === 'new') {
      if (!this.newName || !this.newEmail || !this.newPhone || !this.newIdNumber) {
        alert('Please fill in all required person identity fields.');
        return;
      }

      const body = {
        customerId: parseInt(this.selectedCustomerId, 10),
        name: this.newName.trim(),
        email: this.newEmail.trim(),
        phone: this.newPhone.trim(),
        idType: this.newIdType,
        idNumber: this.newIdNumber.trim()
      };

      this.admin.addAttendant(body).subscribe({
        next: (res: any) => {
          this.closeAddModal();
          this.processBookingAssignment(res.personId);
        },
        error: (err) => alert('Failed to create person: ' + (err.error?.message || err.message))
      });
    } else {
      if (!this.selectedExistingPersonId) {
        alert('Please select an existing company attendant.');
        return;
      }
      this.closeAddModal();
      this.processBookingAssignment(this.selectedExistingPersonId);
    }
  }

  processBookingAssignment(personId: number) {
    const bId = this.selectedBookingDetailId!;
    const cId = parseInt(this.selectedCustomerId, 10);

    this.admin.checkAttendantCapacity(bId).subscribe({
      next: (cap: any) => {
        this.pendingAssignmentPayload = {
          bookingDetailId: bId,
          personId: personId,
          customerId: cId,
          assignedFrom: new Date().toISOString()
        };

        if (cap.wouldExceedCapacity) {
          if (!cap.allowsOverCapacity) {
            alert(\`Over-capacity assignment is strictly not allowed for \${cap.spaceCategory} (\${cap.spaceName}). Capacity limit is \${cap.roomCapacity}.\`);
            return;
          }

          this.estimatedSurcharge = cap.estimatedSurcharge;
          this.seatPrice = cap.seatPrice;
          this.warningModalText = \`Adding this attendant to \${cap.spaceName} exceeds the seat capacity (\${cap.roomCapacity} seats). An over-capacity surcharge of $\${cap.estimatedSurcharge} will be applied.\`;
          this.showWarningModal = true;
        } else {
          this.executeAssignment();
        }
      }
    });
  }

  confirmOverCapacityAssignment() {
    this.showWarningModal = false;
    this.executeAssignment();
  }

  executeAssignment() {
    if (!this.pendingAssignmentPayload) return;
    this.admin.assignAttendantToBooking(this.pendingAssignmentPayload.bookingDetailId, this.pendingAssignmentPayload).subscribe({
      next: () => {
        this.pendingAssignmentPayload = null;
        this.onCustomerChange();
      },
      error: (err) => alert('Assignment failed: ' + (err.error?.message || err.message))
    });
  }

  toggleIndividualAccess(attendant: any, event: any) {
    const isEnabled = event.target.checked;
    const body = {
      bookingDetailId: this.selectedBookingDetailId,
      customerId: parseInt(this.selectedCustomerId, 10),
      personId: attendant.personId,
      isEnabled: isEnabled
    };
    this.admin.toggleAccessStatus(body).subscribe({
      error: () => {
        alert('Failed to update access status');
        this.loadAttendantsForSpace();
      }
    });
  }

  toggleBatchAccess(event: any) {
    const isEnabled = event.target.checked;
    const body = {
      bookingDetailId: this.selectedBookingDetailId,
      customerId: parseInt(this.selectedCustomerId, 10),
      personId: null,
      isEnabled: isEnabled
    };
    this.admin.toggleAccessStatus(body).subscribe({
      next: () => this.loadAttendantsForSpace(),
      error: () => alert('Failed to update batch access status')
    });
  }

  removeAttendant(personId: number) {
    if (!confirm('Are you sure you want to remove this attendant from this booking assignment?')) return;
    this.admin.removeAttendantFromBooking(this.selectedBookingDetailId!, personId).subscribe({
      next: () => this.loadAttendantsForSpace(),
      error: (err) => alert('Failed to remove attendant: ' + (err.error?.message || err.message))
    });
  }

  openExportModal() {
    this.admin.getHikvisionExport().subscribe({
      next: (res: any) => {
        this.exportDataJson = JSON.stringify(res, null, 2);
        this.showExportModal = true;
      },
      error: (err) => alert('Failed to fetch export: ' + (err.error?.message || err.message))
    });
  }

  get allAttendantsEnabled(): boolean {
    const list = this.attendants();
    return list.length > 0 && list.every(a => a.isEnabled);
  }
}
`;

fs.writeFileSync(path.join(compDir, 'attendant-management.ts'), tsContent, 'utf8');

// attendant-management.html
const htmlContent = `<div class="attendant-container">
  <div class="page-header">
    <div>
      <h2>Attendant & Access Control</h2>
      <p class="subtitle">Manage company attendants, room capacity overage billing, and Hikvision fingerprint access status.</p>
    </div>
    <button class="btn btn-outline" (click)="openExportModal()">🔒 Hikvision Export Preview</button>
  </div>

  <div class="grid-container">
    <!-- Sidebar: Steps 1 & 2 -->
    <div class="sidebar-col">
      <div class="card">
        <div class="card-title">Step 1 — Select Customer</div>
        <div class="form-group">
          <label>Customer Company / Name</label>
          <select class="form-control" [(ngModel)]="selectedCustomerId" (change)="onCustomerChange()">
            <option value="">-- Choose Active Customer --</option>
            @for (c of customers(); track c.id) {
              <option [value]="c.id">{{ c.company || c.name || c.email }}</option>
            }
          </select>
        </div>
      </div>

      <div class="card">
        <div class="card-title">Step 2 — Select Booked Space</div>
        @if (activeSpaces().length === 0) {
          <div class="empty-state">Select a customer to view active rooms</div>
        } @else {
          <div class="spaces-list">
            @for (s of activeSpaces(); track s.bookingDetailId) {
              <div class="space-card" [class.active]="selectedBookingDetailId === s.bookingDetailId" (click)="selectSpace(s)">
                <div class="space-title">{{ s.spaceName }}</div>
                <div class="space-meta">{{ s.spaceCategory || 'Dedicated Office' }} • Cap: {{ s.capacity }} seats</div>
                <span class="badge">{{ s.activeAttendantsCount }}/{{ s.capacity }}</span>
              </div>
            }
          </div>
        }
      </div>
    </div>

    <!-- Main Content: Step 3 Attendant Workspace -->
    <div class="main-col">
      <div class="card">
        <div class="card-header-row">
          <div class="card-title">Step 3 — Attendants & Access Status</div>
          <button class="btn btn-primary" [disabled]="!selectedBookingDetailId" (click)="openAddModal()">+ Add Attendant</button>
        </div>

        @if (capacityInfo()) {
          <div class="capacity-box">
            <div class="cap-header">
              <span><strong>Space:</strong> {{ capacityInfo().spaceName }} ({{ capacityInfo().spaceCategory }})</span>
              <span><strong>Seat Capacity:</strong> {{ attendants().length }} / {{ capacityInfo().roomCapacity }}</span>
            </div>
            <div class="cap-bar">
              <div class="cap-fill" [style.width.%]="(attendants().length / capacityInfo().roomCapacity) * 100"></div>
            </div>
            <div class="batch-row">
              <span>Batch Space Access Switch:</span>
              <input type="checkbox" [checked]="allAttendantsEnabled" (change)="toggleBatchAccess($event)">
            </div>
          </div>
        }

        @if (!selectedBookingDetailId) {
          <div class="empty-state">Select a customer and room/space to manage attendants</div>
        } @else if (attendants().length === 0) {
          <div class="empty-state">No attendants assigned to this space yet. Click <strong>+ Add Attendant</strong> above.</div>
        } @else {
          <table class="data-table">
            <thead>
              <tr>
                <th>Attendant Name</th>
                <th>CNIC / Passport</th>
                <th>Contact</th>
                <th>Surcharge</th>
                <th>Access Status</th>
                <th class="text-end">Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (a of attendants(); track a.personId) {
                <tr>
                  <td>
                    <strong>{{ a.name }}</strong><br>
                    <small class="text-muted">{{ a.personGuid.substring(0, 8) }}...</small>
                  </td>
                  <td>{{ a.idType }}: {{ a.idNumber }}</td>
                  <td>{{ a.email }}<br><small class="text-muted">{{ a.phone }}</small></td>
                  <td>
                    @if (a.isOverCapacity) {
                      <span class="badge-surcharge">+${{ a.surchargeApplied || 0 }}</span>
                    } @else {
                      <span class="text-muted">Standard</span>
                    }
                  </td>
                  <td>
                    <input type="checkbox" [checked]="a.isEnabled" (change)="toggleIndividualAccess(a, $event)">
                  </td>
                  <td class="text-end">
                    <button class="btn btn-sm btn-danger" (click)="removeAttendant(a.personId)">Remove</button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>
    </div>
  </div>
</div>

<!-- Modal: Add Attendant -->
@if (showAddModal) {
  <div class="modal-backdrop">
    <div class="modal-card">
      <h3>Add Attendant to Space</h3>
      <div class="form-group">
        <label>Mode</label>
        <select class="form-control" [(ngModel)]="addMode">
          <option value="new">Create New Person Identity</option>
          <option value="existing">Assign Existing Company Attendant</option>
        </select>
      </div>

      @if (addMode === 'existing') {
        <div class="form-group">
          <label>Company Attendant</label>
          <select class="form-control" [(ngModel)]="selectedExistingPersonId">
            <option [value]="null">-- Select Attendant --</option>
            @for (p of existingCompanyAttendants(); track p.personId) {
              <option [value]="p.personId">{{ p.name }} ({{ p.idType }}: {{ p.idNumber }})</option>
            }
          </select>
        </div>
      } @else {
        <div class="form-group">
          <label>Full Name</label>
          <input type="text" class="form-control" [(ngModel)]="newName" placeholder="John Doe">
        </div>
        <div class="form-group">
          <label>Email</label>
          <input type="email" class="form-control" [(ngModel)]="newEmail" placeholder="john@company.com">
        </div>
        <div class="form-group">
          <label>Phone</label>
          <input type="text" class="form-control" [(ngModel)]="newPhone" placeholder="+92 300 1234567">
        </div>
        <div class="grid-2">
          <div class="form-group">
            <label>ID Type</label>
            <select class="form-control" [(ngModel)]="newIdType">
              <option value="CNIC">CNIC</option>
              <option value="Passport">Passport</option>
            </select>
          </div>
          <div class="form-group">
            <label>ID Number</label>
            <input type="text" class="form-control" [(ngModel)]="newIdNumber" placeholder="61101-1234567-1">
          </div>
        </div>
      }

      <div class="modal-actions">
        <button class="btn btn-outline" (click)="closeAddModal()">Cancel</button>
        <button class="btn btn-primary" (click)="submitAddAttendant()">Proceed</button>
      </div>
    </div>
  </div>
}

<!-- Modal: Over Capacity Warning -->
@if (showWarningModal) {
  <div class="modal-backdrop">
    <div class="modal-card">
      <h3 style="color: #f59e0b;">⚠️ Room Capacity Reached</h3>
      <div class="warning-alert">
        <p>{{ warningModalText }}</p>
      </div>
      <p><small class="text-muted">Surcharge calculation: ExcessSeatCount × 0.5 × SeatPrice. Surcharges remain snapshot-billed for the active cycle once applied.</small></p>
      <div class="modal-actions">
        <button class="btn btn-outline" (click)="showWarningModal = false">Cancel</button>
        <button class="btn btn-warning" (click)="confirmOverCapacityAssignment()">Confirm & Apply Surcharge</button>
      </div>
    </div>
  </div>
}

<!-- Modal: Hikvision Export Preview -->
@if (showExportModal) {
  <div class="modal-backdrop">
    <div class="modal-card" style="max-width: 650px;">
      <h3>Hikvision Export Preview</h3>
      <pre class="json-code">{{ exportDataJson }}</pre>
      <div class="modal-actions">
        <button class="btn btn-outline" (click)="showExportModal = false">Close</button>
      </div>
    </div>
  </div>
}
`;

fs.writeFileSync(path.join(compDir, 'attendant-management.html'), htmlContent, 'utf8');

// attendant-management.css
const cssContent = `.attendant-container { padding: 1.5rem; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem; }
.subtitle { color: #64748b; font-size: 0.9rem; }
.grid-container { display: grid; grid-template-columns: 320px 1fr; gap: 1.5rem; }
.card { background: #ffffff; border: 1px solid #e2e8f0; border-radius: 12px; padding: 1.25rem; margin-bottom: 1rem; box-shadow: 0 1px 3px rgba(0,0,0,0.05); }
.card-title { font-size: 1rem; font-weight: 600; margin-bottom: 1rem; color: #0f172a; }
.card-header-row { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
.form-group { margin-bottom: 1rem; }
.form-group label { display: block; font-size: 0.8rem; font-weight: 600; color: #475569; margin-bottom: 0.3rem; }
.form-control { width: 100%; padding: 0.5rem 0.75rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 0.9rem; }
.space-card { border: 1px solid #e2e8f0; border-radius: 8px; padding: 0.75rem; margin-bottom: 0.5rem; cursor: pointer; transition: all 0.2s; }
.space-card:hover, .space-card.active { border-color: #0ea5e9; background: #f0f9ff; }
.space-title { font-weight: 600; font-size: 0.9rem; }
.space-meta { font-size: 0.75rem; color: #64748b; }
.badge { font-size: 0.75rem; background: #e2e8f0; padding: 0.1rem 0.4rem; border-radius: 4px; font-weight: 600; }
.badge-surcharge { background: #fef3c7; color: #d97706; font-size: 0.75rem; padding: 0.2rem 0.5rem; border-radius: 4px; font-weight: 600; }
.capacity-box { background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 1rem; margin-bottom: 1rem; }
.cap-header { display: flex; justify-content: space-between; font-size: 0.85rem; margin-bottom: 0.5rem; }
.cap-bar { height: 8px; background: #e2e8f0; border-radius: 4px; overflow: hidden; }
.cap-fill { height: 100%; background: #10b981; }
.batch-row { display: flex; justify-content: space-between; font-size: 0.8rem; margin-top: 0.5rem; color: #475569; }
.data-table { width: 100%; border-collapse: collapse; font-size: 0.85rem; }
.data-table th, .data-table td { padding: 0.75rem; border-bottom: 1px solid #e2e8f0; text-align: left; }
.btn { padding: 0.5rem 1rem; border-radius: 6px; font-size: 0.85rem; font-weight: 600; border: none; cursor: pointer; }
.btn-primary { background: #0ea5e9; color: #fff; }
.btn-warning { background: #f59e0b; color: #000; }
.btn-danger { background: #ef4444; color: #fff; }
.btn-outline { background: transparent; border: 1px solid #cbd5e1; color: #334155; }
.empty-state { text-align: center; padding: 2rem; color: #94a3b8; font-size: 0.9rem; }
.modal-backdrop { position: fixed; top: 0; left: 0; right: 0; bottom: 0; background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 1000; }
.modal-card { background: #fff; border-radius: 12px; padding: 1.5rem; width: 100%; max-width: 480px; box-shadow: 0 10px 25px rgba(0,0,0,0.15); }
.modal-actions { display: flex; justify-content: flex-end; gap: 0.5rem; margin-top: 1rem; }
.warning-alert { background: #fffbeb; border: 1px solid #fde68a; padding: 1rem; border-radius: 8px; color: #92400e; margin-bottom: 1rem; }
.json-code { background: #0f172a; color: #38bdf8; padding: 1rem; border-radius: 8px; max-height: 300px; overflow-y: auto; font-size: 0.8rem; }
.grid-2 { display: grid; grid-template-columns: 100px 1fr; gap: 0.5rem; }
.text-end { text-align: right; }
.text-muted { color: #64748b; }
`;

fs.writeFileSync(path.join(compDir, 'attendant-management.css'), cssContent, 'utf8');

// 3. Update app.routes.ts
const routesPath = path.join(feBase, 'src\\app\\app.routes.ts');
let routesContent = fs.readFileSync(routesPath, 'utf8');
if (!routesContent.includes('attendant-management')) {
    routesContent = routesContent.replace(
        "{ path: 'challan-validity'",
        "{ path: 'attendants', loadComponent: () => import('./pages/admin/attendant-management/attendant-management').then(m => m.AttendantManagement), canActivate: [adminGuard] },\n      { path: 'challan-validity'"
    );
    fs.writeFileSync(routesPath, routesContent, 'utf8');
    console.log('Updated app.routes.ts');
}

// 4. Update admin-layout.ts
const layoutTsPath = path.join(feBase, 'src\\app\\pages\\admin\\layout\\admin-layout.ts');
let layoutTsContent = fs.readFileSync(layoutTsPath, 'utf8');
if (!layoutTsContent.includes("route: '/admin/attendants'")) {
    layoutTsContent = layoutTsContent.replace(
        "{ route: '/admin/challan-validity'",
        "{ route: '/admin/attendants',        label: 'Attendants & Access', icon: 'users' },\n    { route: '/admin/challan-validity'"
    );
    fs.writeFileSync(layoutTsPath, layoutTsContent, 'utf8');
    console.log('Updated admin-layout.ts');
}

console.log('ALL ANGULAR FRONTEND UPDATES COMPLETED SUCCESSFULLY');
