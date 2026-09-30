-- MySQL dump 10.13  Distrib 26.7.0, for macos15 (arm64)
--
-- Host: localhost    Database: team4_zoo_db
-- ------------------------------------------------------
-- Server version	26.7.0-commercial

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!50503 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;

--
-- Current Database: `team4_zoo_db`
--

CREATE DATABASE /*!32312 IF NOT EXISTS*/ `team4_zoo_db` /*!40100 DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci */ /*!80016 DEFAULT ENCRYPTION='N' */;

USE `team4_zoo_db`;

--
-- Table structure for table `Animal`
--

DROP TABLE IF EXISTS `Animal`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Animal` (
  `Animal_ID` int NOT NULL AUTO_INCREMENT,
  `Animal_Name` varchar(100) NOT NULL,
  `Species_ID` int NOT NULL,
  `Gender` varchar(20) DEFAULT NULL,
  `Date_of_Birth` date DEFAULT NULL,
  `Health_Status` varchar(100) DEFAULT NULL,
  `Enclosure_ID` int NOT NULL,
  `Date_Acquired` date DEFAULT NULL,
  `Acquisition_Type` varchar(100) DEFAULT NULL,
  PRIMARY KEY (`Animal_ID`),
  KEY `Species_ID` (`Species_ID`),
  KEY `Enclosure_ID` (`Enclosure_ID`),
  CONSTRAINT `animal_ibfk_1` FOREIGN KEY (`Species_ID`) REFERENCES `Species` (`Species_ID`) ON UPDATE CASCADE,
  CONSTRAINT `animal_ibfk_2` FOREIGN KEY (`Enclosure_ID`) REFERENCES `Enclosure` (`Enclosure_ID`) ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Animal`
--

LOCK TABLES `Animal` WRITE;
/*!40000 ALTER TABLE `Animal` DISABLE KEYS */;
/*!40000 ALTER TABLE `Animal` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Attraction`
--

DROP TABLE IF EXISTS `Attraction`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Attraction` (
  `Attraction_ID` int NOT NULL AUTO_INCREMENT,
  `Attraction_Name` varchar(100) NOT NULL,
  `Attraction_Type` varchar(100) DEFAULT NULL,
  `Location` varchar(100) DEFAULT NULL,
  `Price` decimal(10,2) DEFAULT NULL,
  `Status` varchar(50) DEFAULT NULL,
  PRIMARY KEY (`Attraction_ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Attraction`
--

LOCK TABLES `Attraction` WRITE;
/*!40000 ALTER TABLE `Attraction` DISABLE KEYS */;
/*!40000 ALTER TABLE `Attraction` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Customer`
--

DROP TABLE IF EXISTS `Customer`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Customer` (
  `Customer_ID` int NOT NULL AUTO_INCREMENT,
  `First_Name` varchar(50) NOT NULL,
  `Last_Name` varchar(50) NOT NULL,
  `Email` varchar(100) DEFAULT NULL,
  `Phone_Number` varchar(20) DEFAULT NULL,
  PRIMARY KEY (`Customer_ID`),
  UNIQUE KEY `Email` (`Email`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Customer`
--

LOCK TABLES `Customer` WRITE;
/*!40000 ALTER TABLE `Customer` DISABLE KEYS */;
/*!40000 ALTER TABLE `Customer` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Department`
--

DROP TABLE IF EXISTS `Department`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Department` (
  `Department_ID` int NOT NULL AUTO_INCREMENT,
  `Department_Name` varchar(100) NOT NULL,
  `Location` varchar(100) DEFAULT NULL,
  `Phone_Number` varchar(20) DEFAULT NULL,
  PRIMARY KEY (`Department_ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Department`
--

LOCK TABLES `Department` WRITE;
/*!40000 ALTER TABLE `Department` DISABLE KEYS */;
/*!40000 ALTER TABLE `Department` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Employee`
--

DROP TABLE IF EXISTS `Employee`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Employee` (
  `Employee_ID` int NOT NULL AUTO_INCREMENT,
  `First_Name` varchar(50) NOT NULL,
  `Middle_Name` varchar(50) DEFAULT NULL,
  `Last_Name` varchar(50) NOT NULL,
  `Address` varchar(255) DEFAULT NULL,
  `Gender` varchar(20) DEFAULT NULL,
  `Phone_Number` varchar(20) DEFAULT NULL,
  `Job_Position` varchar(100) DEFAULT NULL,
  `Hire_Date` date DEFAULT NULL,
  `Department_ID` int DEFAULT NULL,
  `Employment_Type` varchar(50) DEFAULT NULL,
  `Pay_Type` varchar(50) DEFAULT NULL,
  `Pay_Rate` decimal(10,2) DEFAULT NULL,
  PRIMARY KEY (`Employee_ID`),
  KEY `Department_ID` (`Department_ID`),
  CONSTRAINT `employee_ibfk_1` FOREIGN KEY (`Department_ID`) REFERENCES `Department` (`Department_ID`) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Employee`
--

LOCK TABLES `Employee` WRITE;
/*!40000 ALTER TABLE `Employee` DISABLE KEYS */;
/*!40000 ALTER TABLE `Employee` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Employee_Attraction`
--

DROP TABLE IF EXISTS `Employee_Attraction`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Employee_Attraction` (
  `Employee_ID` int NOT NULL,
  `Attraction_ID` int NOT NULL,
  `Assignment_Date` date DEFAULT NULL,
  `Role` varchar(100) DEFAULT NULL,
  PRIMARY KEY (`Employee_ID`,`Attraction_ID`),
  KEY `Attraction_ID` (`Attraction_ID`),
  CONSTRAINT `employee_attraction_ibfk_1` FOREIGN KEY (`Employee_ID`) REFERENCES `Employee` (`Employee_ID`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `employee_attraction_ibfk_2` FOREIGN KEY (`Attraction_ID`) REFERENCES `Attraction` (`Attraction_ID`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Employee_Attraction`
--

LOCK TABLES `Employee_Attraction` WRITE;
/*!40000 ALTER TABLE `Employee_Attraction` DISABLE KEYS */;
/*!40000 ALTER TABLE `Employee_Attraction` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Enclosure`
--

DROP TABLE IF EXISTS `Enclosure`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Enclosure` (
  `Enclosure_ID` int NOT NULL AUTO_INCREMENT,
  `Enclosure_Name` varchar(100) NOT NULL,
  `Enclosure_Type` varchar(100) DEFAULT NULL,
  `Location` varchar(100) DEFAULT NULL,
  `Capacity` int NOT NULL,
  PRIMARY KEY (`Enclosure_ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Enclosure`
--

LOCK TABLES `Enclosure` WRITE;
/*!40000 ALTER TABLE `Enclosure` DISABLE KEYS */;
/*!40000 ALTER TABLE `Enclosure` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Food_Item`
--

DROP TABLE IF EXISTS `Food_Item`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Food_Item` (
  `FoodItem_ID` int NOT NULL AUTO_INCREMENT,
  `Stand_ID` int NOT NULL,
  `FoodItem_Name` varchar(100) NOT NULL,
  `Description` varchar(255) DEFAULT NULL,
  `Price` decimal(10,2) NOT NULL,
  `Category` varchar(50) DEFAULT NULL,
  PRIMARY KEY (`FoodItem_ID`),
  KEY `Stand_ID` (`Stand_ID`),
  CONSTRAINT `food_item_ibfk_1` FOREIGN KEY (`Stand_ID`) REFERENCES `Food_Stand` (`Stand_ID`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Food_Item`
--

LOCK TABLES `Food_Item` WRITE;
/*!40000 ALTER TABLE `Food_Item` DISABLE KEYS */;
/*!40000 ALTER TABLE `Food_Item` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Food_Stand`
--

DROP TABLE IF EXISTS `Food_Stand`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Food_Stand` (
  `Stand_ID` int NOT NULL AUTO_INCREMENT,
  `Stand_Name` varchar(100) NOT NULL,
  `Description` varchar(255) DEFAULT NULL,
  `Location` varchar(100) DEFAULT NULL,
  `Category` varchar(50) DEFAULT NULL,
  `Opening_Time` time DEFAULT NULL,
  `Closing_Time` time DEFAULT NULL,
  PRIMARY KEY (`Stand_ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Food_Stand`
--

LOCK TABLES `Food_Stand` WRITE;
/*!40000 ALTER TABLE `Food_Stand` DISABLE KEYS */;
/*!40000 ALTER TABLE `Food_Stand` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Gift_Shop`
--

DROP TABLE IF EXISTS `Gift_Shop`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Gift_Shop` (
  `GiftShop_ID` int NOT NULL AUTO_INCREMENT,
  `GiftShop_Name` varchar(100) NOT NULL,
  `Category` varchar(50) DEFAULT NULL,
  `Location` varchar(100) DEFAULT NULL,
  `Opening_Time` time DEFAULT NULL,
  `Closing_Time` time DEFAULT NULL,
  PRIMARY KEY (`GiftShop_ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Gift_Shop`
--

LOCK TABLES `Gift_Shop` WRITE;
/*!40000 ALTER TABLE `Gift_Shop` DISABLE KEYS */;
/*!40000 ALTER TABLE `Gift_Shop` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Inventory`
--

DROP TABLE IF EXISTS `Inventory`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Inventory` (
  `Inventory_ID` int NOT NULL AUTO_INCREMENT,
  `Product_ID` int NOT NULL,
  `Quantity_In_Stock` int NOT NULL DEFAULT '0',
  `Reorder_Level` int NOT NULL DEFAULT '0',
  `Last_Updated` datetime DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`Inventory_ID`),
  UNIQUE KEY `Product_ID` (`Product_ID`),
  CONSTRAINT `inventory_ibfk_1` FOREIGN KEY (`Product_ID`) REFERENCES `Product` (`Product_ID`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Inventory`
--

LOCK TABLES `Inventory` WRITE;
/*!40000 ALTER TABLE `Inventory` DISABLE KEYS */;
/*!40000 ALTER TABLE `Inventory` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Product`
--

DROP TABLE IF EXISTS `Product`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Product` (
  `Product_ID` int NOT NULL AUTO_INCREMENT,
  `GiftShop_ID` int NOT NULL,
  `Product_Name` varchar(100) NOT NULL,
  `Description` varchar(255) DEFAULT NULL,
  `Category` varchar(50) DEFAULT NULL,
  `Price` decimal(10,2) NOT NULL,
  PRIMARY KEY (`Product_ID`),
  KEY `GiftShop_ID` (`GiftShop_ID`),
  CONSTRAINT `product_ibfk_1` FOREIGN KEY (`GiftShop_ID`) REFERENCES `Gift_Shop` (`GiftShop_ID`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Product`
--

LOCK TABLES `Product` WRITE;
/*!40000 ALTER TABLE `Product` DISABLE KEYS */;
/*!40000 ALTER TABLE `Product` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Sale`
--

DROP TABLE IF EXISTS `Sale`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Sale` (
  `Sale_ID` int NOT NULL AUTO_INCREMENT,
  `Sale_Date` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `Employee_ID` int DEFAULT NULL,
  `Customer_ID` int DEFAULT NULL,
  `Stand_ID` int DEFAULT NULL,
  `GiftShop_ID` int DEFAULT NULL,
  `Sale_Type` varchar(20) NOT NULL,
  `Total_Amount` decimal(10,2) NOT NULL,
  `Payment_Method` varchar(50) DEFAULT NULL,
  PRIMARY KEY (`Sale_ID`),
  KEY `Employee_ID` (`Employee_ID`),
  KEY `Customer_ID` (`Customer_ID`),
  KEY `Stand_ID` (`Stand_ID`),
  KEY `GiftShop_ID` (`GiftShop_ID`),
  CONSTRAINT `sale_ibfk_1` FOREIGN KEY (`Employee_ID`) REFERENCES `Employee` (`Employee_ID`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `sale_ibfk_2` FOREIGN KEY (`Customer_ID`) REFERENCES `Customer` (`Customer_ID`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `sale_ibfk_3` FOREIGN KEY (`Stand_ID`) REFERENCES `Food_Stand` (`Stand_ID`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `sale_ibfk_4` FOREIGN KEY (`GiftShop_ID`) REFERENCES `Gift_Shop` (`GiftShop_ID`) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Sale`
--

LOCK TABLES `Sale` WRITE;
/*!40000 ALTER TABLE `Sale` DISABLE KEYS */;
/*!40000 ALTER TABLE `Sale` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Sale_Item`
--

DROP TABLE IF EXISTS `Sale_Item`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Sale_Item` (
  `SaleItem_ID` int NOT NULL AUTO_INCREMENT,
  `Sale_ID` int NOT NULL,
  `Product_ID` int DEFAULT NULL,
  `FoodItem_ID` int DEFAULT NULL,
  `Quantity` int NOT NULL,
  `Unit_Price` decimal(10,2) NOT NULL,
  PRIMARY KEY (`SaleItem_ID`),
  KEY `Sale_ID` (`Sale_ID`),
  KEY `Product_ID` (`Product_ID`),
  KEY `FoodItem_ID` (`FoodItem_ID`),
  CONSTRAINT `sale_item_ibfk_1` FOREIGN KEY (`Sale_ID`) REFERENCES `Sale` (`Sale_ID`),
  CONSTRAINT `sale_item_ibfk_2` FOREIGN KEY (`Product_ID`) REFERENCES `Product` (`Product_ID`),
  CONSTRAINT `sale_item_ibfk_3` FOREIGN KEY (`FoodItem_ID`) REFERENCES `Food_Item` (`FoodItem_ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Sale_Item`
--

LOCK TABLES `Sale_Item` WRITE;
/*!40000 ALTER TABLE `Sale_Item` DISABLE KEYS */;
/*!40000 ALTER TABLE `Sale_Item` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Species`
--

DROP TABLE IF EXISTS `Species`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Species` (
  `Species_ID` int NOT NULL AUTO_INCREMENT,
  `Common_Name` varchar(100) NOT NULL,
  `Scientific_Name` varchar(150) DEFAULT NULL,
  `Diet_Type` varchar(50) DEFAULT NULL,
  `Conservation_Status` varchar(100) DEFAULT NULL,
  `Habitat_Type` varchar(100) DEFAULT NULL,
  PRIMARY KEY (`Species_ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Species`
--

LOCK TABLES `Species` WRITE;
/*!40000 ALTER TABLE `Species` DISABLE KEYS */;
/*!40000 ALTER TABLE `Species` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Supplier`
--

DROP TABLE IF EXISTS `Supplier`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Supplier` (
  `Supplier_ID` int NOT NULL AUTO_INCREMENT,
  `Supplier_Name` varchar(100) NOT NULL,
  `Phone` varchar(20) DEFAULT NULL,
  `Email` varchar(100) DEFAULT NULL,
  `Address` varchar(255) DEFAULT NULL,
  `Delivery_Schedule` varchar(100) DEFAULT NULL,
  PRIMARY KEY (`Supplier_ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Supplier`
--

LOCK TABLES `Supplier` WRITE;
/*!40000 ALTER TABLE `Supplier` DISABLE KEYS */;
/*!40000 ALTER TABLE `Supplier` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Supplier_Product`
--

DROP TABLE IF EXISTS `Supplier_Product`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Supplier_Product` (
  `Supplier_ID` int NOT NULL,
  `Product_ID` int NOT NULL,
  `Unit_Cost` decimal(10,2) DEFAULT NULL,
  `Availability` varchar(50) DEFAULT NULL,
  `Delivery_Schedule` varchar(100) DEFAULT NULL,
  PRIMARY KEY (`Supplier_ID`,`Product_ID`),
  KEY `Product_ID` (`Product_ID`),
  CONSTRAINT `supplier_product_ibfk_1` FOREIGN KEY (`Supplier_ID`) REFERENCES `Supplier` (`Supplier_ID`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `supplier_product_ibfk_2` FOREIGN KEY (`Product_ID`) REFERENCES `Product` (`Product_ID`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Supplier_Product`
--

LOCK TABLES `Supplier_Product` WRITE;
/*!40000 ALTER TABLE `Supplier_Product` DISABLE KEYS */;
/*!40000 ALTER TABLE `Supplier_Product` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Ticket_Purchase_Item`
--

DROP TABLE IF EXISTS `Ticket_Purchase_Item`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Ticket_Purchase_Item` (
  `TicketPurchase_ID` int NOT NULL,
  `TicketType_ID` int NOT NULL,
  `Quantity` int NOT NULL,
  `Unit_Price` decimal(10,2) NOT NULL,
  PRIMARY KEY (`TicketPurchase_ID`,`TicketType_ID`),
  KEY `TicketType_ID` (`TicketType_ID`),
  CONSTRAINT `ticket_purchase_item_ibfk_1` FOREIGN KEY (`TicketPurchase_ID`) REFERENCES `Ticket_Sale` (`TicketPurchase_ID`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `ticket_purchase_item_ibfk_2` FOREIGN KEY (`TicketType_ID`) REFERENCES `Ticket_Type` (`TicketType_ID`) ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Ticket_Purchase_Item`
--

LOCK TABLES `Ticket_Purchase_Item` WRITE;
/*!40000 ALTER TABLE `Ticket_Purchase_Item` DISABLE KEYS */;
/*!40000 ALTER TABLE `Ticket_Purchase_Item` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Ticket_Sale`
--

DROP TABLE IF EXISTS `Ticket_Sale`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Ticket_Sale` (
  `TicketPurchase_ID` int NOT NULL AUTO_INCREMENT,
  `Customer_ID` int NOT NULL,
  `Purchase_Date` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `Total_Amount` decimal(10,2) NOT NULL,
  `Payment_Method` varchar(50) DEFAULT NULL,
  PRIMARY KEY (`TicketPurchase_ID`),
  KEY `Customer_ID` (`Customer_ID`),
  CONSTRAINT `ticket_sale_ibfk_1` FOREIGN KEY (`Customer_ID`) REFERENCES `Customer` (`Customer_ID`) ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Ticket_Sale`
--

LOCK TABLES `Ticket_Sale` WRITE;
/*!40000 ALTER TABLE `Ticket_Sale` DISABLE KEYS */;
/*!40000 ALTER TABLE `Ticket_Sale` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Ticket_Type`
--

DROP TABLE IF EXISTS `Ticket_Type`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `Ticket_Type` (
  `TicketType_ID` int NOT NULL AUTO_INCREMENT,
  `TicketType_Name` varchar(50) NOT NULL,
  `Price` decimal(10,2) NOT NULL,
  `Eligibility_Requirements` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`TicketType_ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Ticket_Type`
--

LOCK TABLES `Ticket_Type` WRITE;
/*!40000 ALTER TABLE `Ticket_Type` DISABLE KEYS */;
/*!40000 ALTER TABLE `Ticket_Type` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Dumping routines for database 'team4_zoo_db'
--
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;

-- Dump completed on 2026-09-30 12:12:23
