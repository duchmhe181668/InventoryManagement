/* ============================================
   0) DB
   ============================================ */
IF DB_ID(N'InventoryDB') IS NULL
    CREATE DATABASE InventoryDB;
GO
USE InventoryDB;
GO

/* ============================================
   1) SCHEMA (chỉ tạo nếu chưa có)
   ============================================ */

/* Role */
IF OBJECT_ID('dbo.Role','U') IS NULL
BEGIN
    CREATE TABLE Role(
        roleID INT IDENTITY(1,1) PRIMARY KEY,
        roleName NVARCHAR(100) NOT NULL UNIQUE
    );
END
GO

/* Users */
IF OBJECT_ID('dbo.Users','U') IS NULL
BEGIN
    CREATE TABLE Users(
        userID INT IDENTITY(1,1) PRIMARY KEY,
        username VARCHAR(100) NOT NULL UNIQUE,
        [password] VARCHAR(255) NOT NULL,
        [name] NVARCHAR(150) NOT NULL,
        email VARCHAR(255) NULL,
        phoneNumber VARCHAR(30) NULL,
        roleID INT NOT NULL,
        CONSTRAINT FK_Users_Role FOREIGN KEY(roleID) REFERENCES Role(roleID)
    );
END
GO

/* Store */
IF OBJECT_ID('dbo.Store','U') IS NULL
BEGIN
    CREATE TABLE Store(
        storeID INT IDENTITY(1,1) PRIMARY KEY,
        phoneNumber VARCHAR(30) NULL,
        [address] NVARCHAR(255) NULL
    );
END
GO

/* Customer */
IF OBJECT_ID('dbo.Customer','U') IS NULL
BEGIN
    CREATE TABLE Customer(
        customerID INT IDENTITY(1,1) PRIMARY KEY,
        [name] NVARCHAR(150) NOT NULL,
        phoneNumber VARCHAR(30) NULL,
        email VARCHAR(255) NULL
    );
END
GO

/* Supplier */
IF OBJECT_ID('dbo.Supplier','U') IS NULL
BEGIN
    CREATE TABLE Supplier(
        supplierID INT IDENTITY(1,1) PRIMARY KEY,
        [name] NVARCHAR(150) NOT NULL,
        phoneNumber VARCHAR(30) NULL,
        email VARCHAR(255) NULL,
        address NVARCHAR(255) NULL
    );
END
GO

/* Receipt */
IF OBJECT_ID('dbo.Receipt','U') IS NULL
BEGIN
    CREATE TABLE Receipt(
        receiptID INT IDENTITY(1,1) PRIMARY KEY,
        staffID INT NOT NULL,
        customerID INT NULL,
        discount DECIMAL(18,2) NOT NULL DEFAULT(0),
        createdAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_receipt_staff FOREIGN KEY(staffID) REFERENCES Users(userID),
        CONSTRAINT FK_receipt_customer FOREIGN KEY(customerID) REFERENCES Customer(customerID)
    );
END
GO

/* Category */
IF OBJECT_ID('dbo.Category','U') IS NULL
BEGIN
    CREATE TABLE Category(
        categoryID NVARCHAR(200) PRIMARY KEY,
        categoryName NVARCHAR(200) NOT NULL
    );
END
GO

/* Good */
IF OBJECT_ID('dbo.Good','U') IS NULL
BEGIN
    CREATE TABLE Good(
        goodID INT IDENTITY(1,1) PRIMARY KEY,
        [name] NVARCHAR(200) NOT NULL,
        unit NVARCHAR(50) NOT NULL,
        dateIn DATE,
        imageURL NVARCHAR(MAX),
        categoryID NVARCHAR(200),
        quantity DECIMAL(18,3) NOT NULL DEFAULT(0),
        priceCost DECIMAL(18,2) NOT NULL DEFAULT(0),
        priceSell DECIMAL(18,2) NOT NULL DEFAULT(0),
        storeID INT NOT NULL,
        supplierID INT NULL,
        CONSTRAINT FK_good_store FOREIGN KEY(storeID) REFERENCES Store(storeID),
        CONSTRAINT FK_good_category FOREIGN KEY(categoryID) REFERENCES Category(categoryID),
        CONSTRAINT FK_good_supplier FOREIGN KEY(supplierID) REFERENCES Supplier(supplierID)
    );
END
GO

/* ReceiptDetail */
IF OBJECT_ID('dbo.ReceiptDetail','U') IS NULL
BEGIN
    CREATE TABLE ReceiptDetail(
        receiptID INT NOT NULL,
        goodID INT NOT NULL,
        price DECIMAL(18,2) NOT NULL,
        quantity DECIMAL(18,3) NOT NULL,
        total DECIMAL(18,2) NOT NULL,
        PRIMARY KEY(receiptID, goodID),
        CONSTRAINT FK_receiptDetail_receipt FOREIGN KEY(receiptID) REFERENCES Receipt(receiptID),
        CONSTRAINT FK_receiptDetail_good FOREIGN KEY(goodID) REFERENCES Good(goodID)
    );
END
GO

/* Orders */
IF OBJECT_ID('dbo.Orders','U') IS NULL
BEGIN
    CREATE TABLE Orders(
        orderID INT IDENTITY(1,1) PRIMARY KEY,
        managerID INT NOT NULL,
        supplierID INT NOT NULL,
        createdAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_order_manager FOREIGN KEY(managerID) REFERENCES Users(userID),
        CONSTRAINT FK_order_supplier FOREIGN KEY(supplierID) REFERENCES Supplier(supplierID)
    );
END
GO

/* OrderDetail */
IF OBJECT_ID('dbo.OrderDetail','U') IS NULL
BEGIN
    CREATE TABLE OrderDetail(
        orderID INT NOT NULL,
        storeID INT NOT NULL,
        goodID INT NOT NULL,
        quantity DECIMAL(18,3) NOT NULL,
        price DECIMAL(18,2) NOT NULL,
        total DECIMAL(18,2) NOT NULL,
        PRIMARY KEY(orderID, goodID),
        CONSTRAINT FK_orderDetail_order FOREIGN KEY(orderID) REFERENCES Orders(orderID),
        CONSTRAINT FK_orderDetail_store FOREIGN KEY(storeID) REFERENCES Store(storeID),
        CONSTRAINT FK_orderDetail_good FOREIGN KEY(goodID) REFERENCES Good(goodID)
    );
END
GO

/* Outbound */
IF OBJECT_ID('dbo.Outbound','U') IS NULL
BEGIN
    CREATE TABLE Outbound(
        outboundID INT IDENTITY(1,1) PRIMARY KEY,
        staffID INT NOT NULL,
        createdAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_outbound_staff FOREIGN KEY(staffID) REFERENCES Users(userID)
    );
END
GO

/* OutboundDetail */
IF OBJECT_ID('dbo.OutboundDetail','U') IS NULL
BEGIN
    CREATE TABLE OutboundDetail(
        outboundID INT NOT NULL,
        goodID INT NOT NULL,
        quantity DECIMAL(18,3) NOT NULL,
        total DECIMAL(18,2) NOT NULL,
        PRIMARY KEY(outboundID, goodID),
        CONSTRAINT FK_outboundDetail_outbound FOREIGN KEY(outboundID) REFERENCES Outbound(outboundID),
        CONSTRAINT FK_outboundDetail_good FOREIGN KEY(goodID) REFERENCES Good(goodID)
    );
END
GO

/* Report */
IF OBJECT_ID('dbo.Report','U') IS NULL
BEGIN
    CREATE TABLE Report(
        [date] DATE PRIMARY KEY,
        revenue DECIMAL(18,2) NOT NULL DEFAULT(0),
        cost DECIMAL(18,2) NOT NULL DEFAULT(0)
    );
END
GO

/* ============================================
   2) SEED DATA (idempotent tối đa có thể)
   ============================================ */

-- Role: 4 role cốt lõi
IF NOT EXISTS (SELECT 1 FROM Role WHERE roleName=N'admin')
INSERT INTO Role(roleName) VALUES (N'admin');
IF NOT EXISTS (SELECT 1 FROM Role WHERE roleName=N'warehouse manager')
INSERT INTO Role(roleName) VALUES (N'warehouse manager');
IF NOT EXISTS (SELECT 1 FROM Role WHERE roleName=N'store manager')
INSERT INTO Role(roleName) VALUES (N'store manager');
IF NOT EXISTS (SELECT 1 FROM Role WHERE roleName=N'supplier')
INSERT INTO Role(roleName) VALUES (N'supplier');

-- Users cơ bản
IF NOT EXISTS (SELECT 1 FROM Users WHERE username='admin1')
INSERT INTO Users(username,[password],[name],email,phoneNumber,roleID)
SELECT 'admin1','123456',N'Nguyễn Văn Admin','admin@example.com','0909000001',r.roleID
FROM Role r WHERE r.roleName=N'admin';

IF NOT EXISTS (SELECT 1 FROM Users WHERE username='wm1')
INSERT INTO Users(username,[password],[name],email,phoneNumber,roleID)
SELECT 'wm1','123456',N'Lê Văn Kho','wm@example.com','0909000002',r.roleID
FROM Role r WHERE r.roleName=N'warehouse manager';

IF NOT EXISTS (SELECT 1 FROM Users WHERE username='sm1')
INSERT INTO Users(username,[password],[name],email,phoneNumber,roleID)
SELECT 'sm1','123456',N'Trần Văn Cửa Hàng','sm@example.com','0909000003',r.roleID
FROM Role r WHERE r.roleName=N'store manager';

IF NOT EXISTS (SELECT 1 FROM Users WHERE username='sup1')
INSERT INTO Users(username,[password],[name],email,phoneNumber,roleID)
SELECT 'sup1','123456',N'Nguyễn Văn NCC','sup@example.com','0909000004',r.roleID
FROM Role r WHERE r.roleName=N'supplier';

-- Users mở rộng
INSERT INTO Users(username,[password],[name],email,phoneNumber,roleID)
SELECT v.username, v.[password], v.[name], v.email, v.phoneNumber, r.roleID
FROM (VALUES
  ('wm2','123456',N'Phạm Quang Kho 2','wm2@example.com','0909000012',N'warehouse manager'),
  ('wm3','123456',N'Đỗ Minh Kho 3','wm3@example.com','0909000013',N'warehouse manager'),
  ('sm2','123456',N'Phạm Thị Cửa Hàng 2','sm2@example.com','0909000014',N'store manager'),
  ('sm3','123456',N'Hoàng Văn Cửa Hàng 3','sm3@example.com','0909000015',N'store manager'),
  ('sup2','123456',N'Ngô Thị NCC 2','sup2@example.com','0909000016',N'supplier'),
  ('admin2','123456',N'Admin Phụ 2','admin2@example.com','0909000017',N'admin')
) AS v(username,[password],[name],email,phoneNumber,roleName)
JOIN Role r ON r.roleName = v.roleName
LEFT JOIN Users u ON u.username = v.username
WHERE u.userID IS NULL;

-- Store
INSERT INTO Store(phoneNumber,[address]) SELECT '0281111111',N'Kho trung tâm'
WHERE NOT EXISTS (SELECT 1 FROM Store WHERE [address]=N'Kho trung tâm');
INSERT INTO Store(phoneNumber,[address]) SELECT '0282222222',N'Cửa hàng Quận 1'
WHERE NOT EXISTS (SELECT 1 FROM Store WHERE [address]=N'Cửa hàng Quận 1');
INSERT INTO Store(phoneNumber,[address]) SELECT '0283333333',N'Cửa hàng Quận 3'
WHERE NOT EXISTS (SELECT 1 FROM Store WHERE [address]=N'Cửa hàng Quận 3');
INSERT INTO Store(phoneNumber,[address]) SELECT '0284444444',N'Cửa hàng Thủ Đức'
WHERE NOT EXISTS (SELECT 1 FROM Store WHERE [address]=N'Cửa hàng Thủ Đức');
INSERT INTO Store(phoneNumber,[address]) SELECT '0285555555',N'Kho Bình Tân'
WHERE NOT EXISTS (SELECT 1 FROM Store WHERE [address]=N'Kho Bình Tân');

-- Customer
INSERT INTO Customer([name],phoneNumber,email) SELECT N'Nguyễn Thị Khách','0911000001','khach1@example.com'
WHERE NOT EXISTS (SELECT 1 FROM Customer WHERE [name]=N'Nguyễn Thị Khách');
INSERT INTO Customer([name],phoneNumber,email) SELECT N'Trần Văn Khách','0911000002','khach2@example.com'
WHERE NOT EXISTS (SELECT 1 FROM Customer WHERE [name]=N'Trần Văn Khách');
INSERT INTO Customer([name],phoneNumber,email) SELECT N'Bùi Anh Khách','0911000003','khach3@example.com'
WHERE NOT EXISTS (SELECT 1 FROM Customer WHERE [name]=N'Bùi Anh Khách');
INSERT INTO Customer([name],phoneNumber,email) SELECT N'Đinh Mỹ Khách','0911000004','khach4@example.com'
WHERE NOT EXISTS (SELECT 1 FROM Customer WHERE [name]=N'Đinh Mỹ Khách');
INSERT INTO Customer([name],phoneNumber,email) SELECT N'La Gia Khách','0911000005','khach5@example.com'
WHERE NOT EXISTS (SELECT 1 FROM Customer WHERE [name]=N'La Gia Khách');
INSERT INTO Customer([name],phoneNumber,email) SELECT N'Lương Phú Khách','0911000008','khach8@example.com'
WHERE NOT EXISTS (SELECT 1 FROM Customer WHERE [name]=N'Lương Phú Khách');

-- Supplier
INSERT INTO Supplier([name],phoneNumber,email,address) SELECT N'Công ty Sữa ABC','0933000001','abc@example.com',N'Hà Nội'
WHERE NOT EXISTS (SELECT 1 FROM Supplier WHERE [name]=N'Công ty Sữa ABC');
INSERT INTO Supplier([name],phoneNumber,email,address) SELECT N'Công ty Bánh Kẹo XYZ','0933000002','xyz@example.com',N'Hồ Chí Minh'
WHERE NOT EXISTS (SELECT 1 FROM Supplier WHERE [name]=N'Công ty Bánh Kẹo XYZ');
INSERT INTO Supplier([name],phoneNumber,email,address) SELECT N'Nhà máy Nước Giải Khát MNP','0933000003','mnp@example.com',N'Đà Nẵng'
WHERE NOT EXISTS (SELECT 1 FROM Supplier WHERE [name]=N'Nhà máy Nước Giải Khát MNP');
INSERT INTO Supplier([name],phoneNumber,email,address) SELECT N'CTCP Gia Vị Hương Việt','0933000004','huongviet@example.com',N'Nha Trang'
WHERE NOT EXISTS (SELECT 1 FROM Supplier WHERE [name]=N'CTCP Gia Vị Hương Việt');
INSERT INTO Supplier([name],phoneNumber,email,address) SELECT N'CTCP Thực Phẩm Tươi QRS','0933000005','qrs@example.com',N'Hải Phòng'
WHERE NOT EXISTS (SELECT 1 FROM Supplier WHERE [name]=N'CTCP Thực Phẩm Tươi QRS');

-- Category
INSERT INTO Category(categoryID,categoryName) SELECT 'CAT01',N'Sữa'
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE categoryID='CAT01');
INSERT INTO Category(categoryID,categoryName) SELECT 'CAT02',N'Bánh'
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE categoryID='CAT02');
INSERT INTO Category(categoryID,categoryName) SELECT 'CAT03',N'Nước giải khát'
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE categoryID='CAT03');
INSERT INTO Category(categoryID,categoryName) SELECT 'CAT04',N'Mì & Bún khô'
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE categoryID='CAT04');
INSERT INTO Category(categoryID,categoryName) SELECT 'CAT05',N'Gia vị'
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE categoryID='CAT05');
INSERT INTO Category(categoryID,categoryName) SELECT 'CAT06',N'Đồ đóng hộp'
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE categoryID='CAT06');

-- Tham chiếu nhanh
IF OBJECT_ID('tempdb..#Ref') IS NOT NULL DROP TABLE #Ref;
CREATE TABLE #Ref(
  store1 INT, store2 INT, store3 INT, store4 INT, store5 INT,
  sup1 INT, sup2 INT, sup3 INT, sup4 INT, sup5 INT,
  wm1 INT, wm2 INT, wm3 INT, sm1 INT, sm2 INT, sm3 INT,
  c1 INT, c2 INT, c3 INT, c4 INT, c5 INT, c6 INT
);
INSERT INTO #Ref(store1,store2,store3,store4,store5,
                 sup1,sup2,sup3,sup4,sup5,
                 wm1,wm2,wm3,sm1,sm2,sm3,
                 c1,c2,c3,c4,c5,c6)
SELECT
 (SELECT storeID FROM Store WHERE [address]=N'Kho trung tâm'),
 (SELECT storeID FROM Store WHERE [address]=N'Cửa hàng Quận 1'),
 (SELECT storeID FROM Store WHERE [address]=N'Cửa hàng Quận 3'),
 (SELECT storeID FROM Store WHERE [address]=N'Cửa hàng Thủ Đức'),
 (SELECT storeID FROM Store WHERE [address]=N'Kho Bình Tân'),
 (SELECT supplierID FROM Supplier WHERE [name]=N'Công ty Sữa ABC'),
 (SELECT supplierID FROM Supplier WHERE [name]=N'Công ty Bánh Kẹo XYZ'),
 (SELECT supplierID FROM Supplier WHERE [name]=N'Nhà máy Nước Giải Khát MNP'),
 (SELECT supplierID FROM Supplier WHERE [name]=N'CTCP Gia Vị Hương Việt'),
 (SELECT supplierID FROM Supplier WHERE [name]=N'CTCP Thực Phẩm Tươi QRS'),
 (SELECT userID FROM Users WHERE username='wm1'),
 (SELECT userID FROM Users WHERE username='wm2'),
 (SELECT userID FROM Users WHERE username='wm3'),
 (SELECT userID FROM Users WHERE username='sm1'),
 (SELECT userID FROM Users WHERE username='sm2'),
 (SELECT userID FROM Users WHERE username='sm3'),
 (SELECT customerID FROM Customer WHERE [name]=N'Nguyễn Thị Khách'),
 (SELECT customerID FROM Customer WHERE [name]=N'Trần Văn Khách'),
 (SELECT customerID FROM Customer WHERE [name]=N'Bùi Anh Khách'),
 (SELECT customerID FROM Customer WHERE [name]=N'Đinh Mỹ Khách'),
 (SELECT customerID FROM Customer WHERE [name]=N'La Gia Khách'),
 (SELECT customerID FROM Customer WHERE [name]=N'Lương Phú Khách');

-- Goods (chỉ thêm nếu chưa có theo tên + store)
INSERT INTO Good([name],unit,dateIn,imageURL,categoryID,quantity,priceCost,priceSell,storeID,supplierID)
SELECT N'Sữa tươi 1L',N'Hộp',GETDATE(),NULL,'CAT01',100,20000,25000,r.store1,r.sup1
FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Good WHERE [name]=N'Sữa tươi 1L' AND storeID=r.store1);

INSERT INTO Good([name],unit,dateIn,imageURL,categoryID,quantity,priceCost,priceSell,storeID,supplierID)
SELECT N'Bánh quy socola',N'Hộp',GETDATE(),NULL,'CAT02',50,30000,40000,r.store2,r.sup2
FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Good WHERE [name]=N'Bánh quy socola' AND storeID=r.store2);

INSERT INTO Good([name],unit,dateIn,imageURL,categoryID,quantity,priceCost,priceSell,storeID,supplierID)
SELECT N'Sữa chua có đường 100g',N'Hộp',GETDATE(),NULL,'CAT01',300,6000,8000,r.store1,r.sup1
FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Good WHERE [name]=N'Sữa chua có đường 100g' AND storeID=r.store1);

INSERT INTO Good([name],unit,dateIn,imageURL,categoryID,quantity,priceCost,priceSell,storeID,supplierID)
SELECT N'Sữa chua không đường 100g',N'Hộp',GETDATE(),NULL,'CAT01',200,6500,8500,r.store2,r.sup1
FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Good WHERE [name]=N'Sữa chua không đường 100g' AND storeID=r.store2);

INSERT INTO Good([name],unit,dateIn,imageURL,categoryID,quantity,priceCost,priceSell,storeID,supplierID)
SELECT N'Bánh quy bơ',N'Gói',GETDATE(),NULL,'CAT02',180,20000,27000,r.store3,r.sup2
FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Good WHERE [name]=N'Bánh quy bơ' AND storeID=r.store3);

INSERT INTO Good([name],unit,dateIn,imageURL,categoryID,quantity,priceCost,priceSell,storeID,supplierID)
SELECT N'Bánh snack rong biển',N'Gói',GETDATE(),NULL,'CAT02',220,12000,17000,r.store4,r.sup2
FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Good WHERE [name]=N'Bánh snack rong biển' AND storeID=r.store4);

INSERT INTO Good([name],unit,dateIn,imageURL,categoryID,quantity,priceCost,priceSell,storeID,supplierID)
SELECT N'Nước ngọt cola 330ml',N'Lon',GETDATE(),NULL,'CAT03',500,7000,10000,r.store1,r.sup3
FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Good WHERE [name]=N'Nước ngọt cola 330ml' AND storeID=r.store1);

INSERT INTO Good([name],unit,dateIn,imageURL,categoryID,quantity,priceCost,priceSell,storeID,supplierID)
SELECT N'Nước cam 350ml',N'Chai',GETDATE(),NULL,'CAT03',400,8000,12000,r.store2,r.sup3
FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Good WHERE [name]=N'Nước cam 350ml' AND storeID=r.store2);

INSERT INTO Good([name],unit,dateIn,imageURL,categoryID,quantity,priceCost,priceSell,storeID,supplierID)
SELECT N'Mì gói chay',N'Gói',GETDATE(),NULL,'CAT04',1000,3500,5000,r.store5,r.sup5
FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Good WHERE [name]=N'Mì gói chay' AND storeID=r.store5);

INSERT INTO Good([name],unit,dateIn,imageURL,categoryID,quantity,priceCost,priceSell,storeID,supplierID)
SELECT N'Bún gạo 500g',N'Gói',GETDATE(),NULL,'CAT04',300,15000,21000,r.store1,r.sup5
FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Good WHERE [name]=N'Bún gạo 500g' AND storeID=r.store1);

INSERT INTO Good([name],unit,dateIn,imageURL,categoryID,quantity,priceCost,priceSell,storeID,supplierID)
SELECT N'Nước mắm 500ml',N'Chai',GETDATE(),NULL,'CAT05',150,18000,26000,r.store3,r.sup4
FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Good WHERE [name]=N'Nước mắm 500ml' AND storeID=r.store3);

INSERT INTO Good([name],unit,dateIn,imageURL,categoryID,quantity,priceCost,priceSell,storeID,supplierID)
SELECT N'Xì dầu 500ml',N'Chai',GETDATE(),NULL,'CAT05',160,17000,24000,r.store4,r.sup4
FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Good WHERE [name]=N'Xì dầu 500ml' AND storeID=r.store4);

INSERT INTO Good([name],unit,dateIn,imageURL,categoryID,quantity,priceCost,priceSell,storeID,supplierID)
SELECT N'Cá hộp 140g',N'Hộp',GETDATE(),NULL,'CAT06',220,16000,23000,r.store2,r.sup5
FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Good WHERE [name]=N'Cá hộp 140g' AND storeID=r.store2);

INSERT INTO Good([name],unit,dateIn,imageURL,categoryID,quantity,priceCost,priceSell,storeID,supplierID)
SELECT N'Dứa đóng hộp 500g',N'Hộp',GETDATE(),NULL,'CAT06',120,30000,42000,r.store5,r.sup5
FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Good WHERE [name]=N'Dứa đóng hộp 500g' AND storeID=r.store5);

/* ============================================
   3) RECEIPT + RECEIPT DETAIL (bán hàng)
   ============================================ */

-- 2 hóa đơn mẫu cũ (đảm bảo tồn tại)
IF NOT EXISTS (SELECT 1 FROM Receipt)
BEGIN
    INSERT INTO Receipt(staffID, customerID, discount)
    SELECT (SELECT userID FROM Users WHERE username='sm1'),
           (SELECT customerID FROM Customer WHERE [name]=N'Nguyễn Thị Khách'),
           0;
    INSERT INTO Receipt(staffID, customerID, discount)
    SELECT (SELECT userID FROM Users WHERE username='sm1'),
           (SELECT customerID FROM Customer WHERE [name]=N'Trần Văn Khách'),
           5000;

    -- Chi tiết cho 2 hóa đơn đầu
    INSERT INTO ReceiptDetail(receiptID, goodID, price, quantity, total)
    SELECT 1, (SELECT goodID FROM Good WHERE [name]=N'Sữa tươi 1L' AND storeID=(SELECT storeID FROM Store WHERE [address]=N'Kho trung tâm')), 25000, 2, 50000
    WHERE NOT EXISTS (SELECT 1 FROM ReceiptDetail WHERE receiptID=1 AND goodID=(SELECT goodID FROM Good WHERE [name]=N'Sữa tươi 1L' AND storeID=(SELECT storeID FROM Store WHERE [address]=N'Kho trung tâm')));

    INSERT INTO ReceiptDetail(receiptID, goodID, price, quantity, total)
    SELECT 1, (SELECT goodID FROM Good WHERE [name]=N'Bánh quy socola' AND storeID=(SELECT storeID FROM Store WHERE [address]=N'Cửa hàng Quận 1')), 40000, 1, 40000
    WHERE NOT EXISTS (SELECT 1 FROM ReceiptDetail WHERE receiptID=1 AND goodID=(SELECT goodID FROM Good WHERE [name]=N'Bánh quy socola' AND storeID=(SELECT storeID FROM Store WHERE [address]=N'Cửa hàng Quận 1')));

    INSERT INTO ReceiptDetail(receiptID, goodID, price, quantity, total)
    SELECT 2, (SELECT goodID FROM Good WHERE [name]=N'Bánh quy socola' AND storeID=(SELECT storeID FROM Store WHERE [address]=N'Cửa hàng Quận 1')), 40000, 2, 80000
    WHERE NOT EXISTS (SELECT 1 FROM ReceiptDetail WHERE receiptID=2 AND goodID=(SELECT goodID FROM Good WHERE [name]=N'Bánh quy socola' AND storeID=(SELECT storeID FROM Store WHERE [address]=N'Cửa hàng Quận 1')));
END

-- Thêm 6 hóa đơn mới (rải theo ngày)
INSERT INTO Receipt(staffID, customerID, discount, createdAt)
SELECT r.sm1, r.c1, 0, DATEADD(DAY,-6, SYSUTCDATETIME()) FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Receipt WHERE staffID=r.sm1 AND customerID=r.c1 AND CAST(createdAt AS DATE)=CAST(DATEADD(DAY,-6,SYSUTCDATETIME()) AS DATE))
UNION ALL SELECT r.sm1, r.c2, 2000, DATEADD(DAY,-5, SYSUTCDATETIME()) FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Receipt WHERE staffID=r.sm1 AND customerID=r.c2 AND CAST(createdAt AS DATE)=CAST(DATEADD(DAY,-5,SYSUTCDATETIME()) AS DATE))
UNION ALL SELECT r.sm2, r.c3, 0, DATEADD(DAY,-4, SYSUTCDATETIME()) FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Receipt WHERE staffID=r.sm2 AND customerID=r.c3 AND CAST(createdAt AS DATE)=CAST(DATEADD(DAY,-4,SYSUTCDATETIME()) AS DATE))
UNION ALL SELECT r.sm2, r.c4, 5000, DATEADD(DAY,-3, SYSUTCDATETIME()) FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Receipt WHERE staffID=r.sm2 AND customerID=r.c4 AND CAST(createdAt AS DATE)=CAST(DATEADD(DAY,-3,SYSUTCDATETIME()) AS DATE))
UNION ALL SELECT r.sm3, r.c5, 0, DATEADD(DAY,-2, SYSUTCDATETIME()) FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Receipt WHERE staffID=r.sm3 AND customerID=r.c5 AND CAST(createdAt AS DATE)=CAST(DATEADD(DAY,-2,SYSUTCDATETIME()) AS DATE))
UNION ALL SELECT r.sm3, r.c6, 0, DATEADD(DAY,-1, SYSUTCDATETIME()) FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Receipt WHERE staffID=r.sm3 AND customerID=r.c6 AND CAST(createdAt AS DATE)=CAST(DATEADD(DAY,-1,SYSUTCDATETIME()) AS DATE));

-- Lấy 6 hóa đơn mới nhất để thêm 1 dòng đầu tiên
;WITH R AS (
  SELECT TOP (6) receiptID, ROW_NUMBER() OVER (ORDER BY receiptID DESC) AS rn
  FROM Receipt
  ORDER BY receiptID DESC
)
INSERT INTO ReceiptDetail(receiptID, goodID, price, quantity, total)
SELECT r1.receiptID, g1.goodID, 10000, 3, 30000
FROM R r1
JOIN Good g1 ON g1.[name]=N'Nước ngọt cola 330ml'
WHERE r1.rn=1
UNION ALL
SELECT r2.receiptID, g2.goodID, 12000, 2, 24000
FROM R r2
JOIN Good g2 ON g2.[name]=N'Nước cam 350ml'
WHERE r2.rn=2
UNION ALL
SELECT r3.receiptID, g3.goodID, 5000, 5, 25000
FROM R r3
JOIN Good g3 ON g3.[name]=N'Mì gói chay'
WHERE r3.rn=3
UNION ALL
SELECT r4.receiptID, g4.goodID, 21000, 1, 21000
FROM R r4
JOIN Good g4 ON g4.[name]=N'Bún gạo 500g'
WHERE r4.rn=4
UNION ALL
SELECT r5.receiptID, g5.goodID, 26000, 2, 52000
FROM R r5
JOIN Good g5 ON g5.[name]=N'Nước mắm 500ml'
WHERE r5.rn=5
UNION ALL
SELECT r6.receiptID, g6.goodID, 23000, 3, 69000
FROM R r6
JOIN Good g6 ON g6.[name]=N'Cá hộp 140g'
WHERE r6.rn=6
AND NOT EXISTS (SELECT 1 FROM ReceiptDetail rd WHERE rd.receiptID=r6.receiptID AND rd.goodID=g6.goodID);

-- MỖI HÓA ĐƠN THÊM DÒNG THỨ 2 (BẢN SỬA: DÙNG ROW_NUMBER, KHÔNG DÙNG TOP + OFFSET)
;WITH R2 AS (
  SELECT receiptID, ROW_NUMBER() OVER (ORDER BY receiptID DESC) AS rn
  FROM Receipt
)
INSERT INTO ReceiptDetail(receiptID, goodID, price, quantity, total)
SELECT r.receiptID, g.goodID, 17000, 2, 34000
FROM R2 r
JOIN Good g ON g.[name]=N'Bánh snack rong biển'
WHERE r.rn = 1
  AND NOT EXISTS (SELECT 1 FROM ReceiptDetail WHERE receiptID=r.receiptID AND goodID=g.goodID)
UNION ALL
SELECT r.receiptID, g.goodID, 27000, 1, 27000
FROM R2 r
JOIN Good g ON g.[name]=N'Bánh quy bơ'
WHERE r.rn = 2
  AND NOT EXISTS (SELECT 1 FROM ReceiptDetail WHERE receiptID=r.receiptID AND goodID=g.goodID)
UNION ALL
SELECT r.receiptID, g.goodID, 8000, 4, 32000
FROM R2 r
JOIN Good g ON g.[name]=N'Sữa chua có đường 100g'
WHERE r.rn = 3
  AND NOT EXISTS (SELECT 1 FROM ReceiptDetail WHERE receiptID=r.receiptID AND goodID=g.goodID)
UNION ALL
SELECT r.receiptID, g.goodID, 8500, 3, 25500
FROM R2 r
JOIN Good g ON g.[name]=N'Sữa chua không đường 100g'
WHERE r.rn = 4
  AND NOT EXISTS (SELECT 1 FROM ReceiptDetail WHERE receiptID=r.receiptID AND goodID=g.goodID)
UNION ALL
SELECT r.receiptID, g.goodID, 42000, 1, 42000
FROM R2 r
JOIN Good g ON g.[name]=N'Dứa đóng hộp 500g'
WHERE r.rn = 5
  AND NOT EXISTS (SELECT 1 FROM ReceiptDetail WHERE receiptID=r.receiptID AND goodID=g.goodID)
UNION ALL
SELECT r.receiptID, g.goodID, 24000, 1, 24000
FROM R2 r
JOIN Good g ON g.[name]=N'Xì dầu 500ml'
WHERE r.rn = 6
  AND NOT EXISTS (SELECT 1 FROM ReceiptDetail WHERE receiptID=r.receiptID AND goodID=g.goodID);

/* ============================================
   4) ORDERS + ORDER DETAIL (đặt hàng NCC)
   ============================================ */

-- Tạo thêm 4 đơn mới (nếu chưa có theo dấu vết ngày)
INSERT INTO Orders(managerID,supplierID,createdAt)
SELECT r.wm2, r.sup3, DATEADD(DAY,-7, SYSUTCDATETIME()) FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Orders WHERE managerID=r.wm2 AND supplierID=r.sup3 AND CAST(createdAt AS DATE)=CAST(DATEADD(DAY,-7,SYSUTCDATETIME()) AS DATE))
UNION ALL
SELECT r.wm1, r.sup4, DATEADD(DAY,-6, SYSUTCDATETIME()) FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Orders WHERE managerID=r.wm1 AND supplierID=r.sup4 AND CAST(createdAt AS DATE)=CAST(DATEADD(DAY,-6,SYSUTCDATETIME()) AS DATE))
UNION ALL
SELECT r.wm3, r.sup5, DATEADD(DAY,-5, SYSUTCDATETIME()) FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Orders WHERE managerID=r.wm3 AND supplierID=r.sup5 AND CAST(createdAt AS DATE)=CAST(DATEADD(DAY,-5,SYSUTCDATETIME()) AS DATE))
UNION ALL
SELECT r.wm1, r.sup2, DATEADD(DAY,-4, SYSUTCDATETIME()) FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Orders WHERE managerID=r.wm1 AND supplierID=r.sup2 AND CAST(createdAt AS DATE)=CAST(DATEADD(DAY,-4,SYSUTCDATETIME()) AS DATE));

;WITH O AS (
  SELECT TOP (4) orderID, ROW_NUMBER() OVER (ORDER BY orderID DESC) AS rn
  FROM Orders ORDER BY orderID DESC
)
INSERT INTO OrderDetail(orderID, storeID, goodID, quantity, price, total)
SELECT o1.orderID, r.store1, g1.goodID, 300, 7000, 2100000
FROM O o1 JOIN #Ref r ON 1=1
JOIN Good g1 ON g1.[name]=N'Nước ngọt cola 330ml'
WHERE o1.rn=1
  AND NOT EXISTS (SELECT 1 FROM OrderDetail WHERE orderID=o1.orderID AND goodID=g1.goodID)
UNION ALL
SELECT o2.orderID, r.store3, g2.goodID, 200, 17000, 3400000
FROM O o2 JOIN #Ref r ON 1=1
JOIN Good g2 ON g2.[name]=N'Xì dầu 500ml'
WHERE o2.rn=2
  AND NOT EXISTS (SELECT 1 FROM OrderDetail WHERE orderID=o2.orderID AND goodID=g2.goodID)
UNION ALL
SELECT o3.orderID, r.store5, g3.goodID, 500, 3500, 1750000
FROM O o3 JOIN #Ref r ON 1=1
JOIN Good g3 ON g3.[name]=N'Mì gói chay'
WHERE o3.rn=3
  AND NOT EXISTS (SELECT 1 FROM OrderDetail WHERE orderID=o3.orderID AND goodID=g3.goodID)
UNION ALL
SELECT o4.orderID, r.store4, g4.goodID, 150, 20000, 3000000
FROM O o4 JOIN #Ref r ON 1=1
JOIN Good g4 ON g4.[name]=N'Bánh quy bơ'
WHERE o4.rn=4
  AND NOT EXISTS (SELECT 1 FROM OrderDetail WHERE orderID=o4.orderID AND goodID=g4.goodID);

/* ============================================
   5) OUTBOUND + OUTBOUND DETAIL (xuất kho)
   ============================================ */

-- 3 phiếu xuất kho mới
INSERT INTO Outbound(staffID, createdAt)
SELECT r.wm1, DATEADD(DAY,-3, SYSUTCDATETIME()) FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Outbound WHERE staffID=r.wm1 AND CAST(createdAt AS DATE)=CAST(DATEADD(DAY,-3,SYSUTCDATETIME()) AS DATE))
UNION ALL
SELECT r.wm2, DATEADD(DAY,-2, SYSUTCDATETIME()) FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Outbound WHERE staffID=r.wm2 AND CAST(createdAt AS DATE)=CAST(DATEADD(DAY,-2,SYSUTCDATETIME()) AS DATE))
UNION ALL
SELECT r.sm2, DATEADD(DAY,-1, SYSUTCDATETIME()) FROM #Ref r
WHERE NOT EXISTS (SELECT 1 FROM Outbound WHERE staffID=r.sm2 AND CAST(createdAt AS DATE)=CAST(DATEADD(DAY,-1,SYSUTCDATETIME()) AS DATE));

;WITH OB AS (
  SELECT TOP (3) outboundID, ROW_NUMBER() OVER (ORDER BY outboundID DESC) AS rn
  FROM Outbound ORDER BY outboundID DESC
)
INSERT INTO OutboundDetail(outboundID, goodID, quantity, total)
SELECT ob1.outboundID, g1.goodID, 80, 800000
FROM OB ob1 JOIN Good g1 ON g1.[name]=N'Nước ngọt cola 330ml'
WHERE ob1.rn=1
  AND NOT EXISTS (SELECT 1 FROM OutboundDetail WHERE outboundID=ob1.outboundID AND goodID=g1.goodID)
UNION ALL
SELECT ob2.outboundID, g2.goodID, 40, 480000
FROM OB ob2 JOIN Good g2 ON g2.[name]=N'Nước cam 350ml'
WHERE ob2.rn=2
  AND NOT EXISTS (SELECT 1 FROM OutboundDetail WHERE outboundID=ob2.outboundID AND goodID=g2.goodID)
UNION ALL
SELECT ob3.outboundID, g3.goodID, 60, 300000
FROM OB ob3 JOIN Good g3 ON g3.[name]=N'Mì gói chay'
WHERE ob3.rn=3
  AND NOT EXISTS (SELECT 1 FROM OutboundDetail WHERE outboundID=ob3.outboundID AND goodID=g3.goodID);

/* ============================================
   6) REPORT (7 ngày gần nhất)
   ============================================ */
DECLARE @i INT = 6;
WHILE @i >= 0
BEGIN
  DECLARE @d DATE = CAST(DATEADD(DAY, -@i, CAST(GETDATE() AS DATE)) AS DATE);
  IF NOT EXISTS (SELECT 1 FROM Report WHERE [date]=@d)
  BEGIN
    INSERT INTO Report([date], revenue, cost)
    VALUES (@d, 500000 + (@i * 35000), 300000 + (@i * 20000));
  END
  SET @i = @i - 1;
END

/* ============================================
   7) CLEANUP
   ============================================ */
DROP TABLE IF EXISTS #Ref;
GO
