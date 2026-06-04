using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YIS.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MENU_OPTIONS",
                columns: table => new
                {
                    MO_ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MO_NAME = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MO_TYPE = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MO_URL = table.Column<string>(type: "nvarchar(500)", nullable: true),
                    MO_DEACTIVE_ON = table.Column<DateTime>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MENU_OPTIONS", x => x.MO_ID);
                });

            migrationBuilder.CreateTable(
                name: "SELLER_MASTER",
                columns: table => new
                {
                    SELLER_ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SELLER_NAME = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SELLER_ADDRESS = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SELLER_CONTACT = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SELLER_EMAIL = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CREATED_BY = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CREATION_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LAST_UPDATED_BY = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LAST_UPDATE_ON = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SELLER_MASTER", x => x.SELLER_ID);
                });

            migrationBuilder.CreateTable(
                name: "USER_ROLE",
                columns: table => new
                {
                    UR_ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UR_NAME = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CREATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    CREATION_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LAST_UPDATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    LAST_UPDATE_ON = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_ROLE", x => x.UR_ID);
                });

            migrationBuilder.CreateTable(
                name: "USER_ROLE_ASSIGNMENTS",
                columns: table => new
                {
                    URA_ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    URA_UM_ID = table.Column<long>(type: "bigint", nullable: true),
                    URA_MO_ID = table.Column<long>(type: "bigint", nullable: true),
                    URA_RIGHTS = table.Column<int>(type: "int", nullable: true),
                    CAN_CREATE = table.Column<bool>(type: "bit", nullable: false),
                    CAN_READ = table.Column<bool>(type: "bit", nullable: false),
                    CAN_UPDATE = table.Column<bool>(type: "bit", nullable: false),
                    CAN_DELETE = table.Column<bool>(type: "bit", nullable: false),
                    URA_DISABLE_ON = table.Column<DateTime>(type: "date", nullable: true),
                    CREATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    CREATION_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LAST_UPDATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    LAST_UPDATE_ON = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_ROLE_ASSIGNMENTS", x => x.URA_ID);
                });

            migrationBuilder.CreateTable(
                name: "USER_COMPANY_PROFILE",
                columns: table => new
                {
                    UCP_ID = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UCP_COMPANY_NTN = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    UCP_COMPANY_NAME = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UCP_COMPANY_PROVINCE = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UCP_COMPANY_ADDRESS = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UCP_IRIS_USERNAME = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UCP_IRIS_PASSWORD = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UCP_SANDBOX_TOKEN = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UCP_PRODUCTION_TOKEN = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UCP_IS_PRODUCTION_TOKEN = table.Column<bool>(type: "bit", nullable: false),
                    UCP_ADD_TAG = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UCP_ADD_STAMP = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UCP_EDIT_TAG = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UCP_EDIT_STAMP = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SELLER_ID = table.Column<long>(type: "bigint", nullable: false),
                    UCP_IS_ACTIVE = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_COMPANY_PROFILE", x => x.UCP_ID);
                    table.ForeignKey(
                        name: "FK_USER_COMPANY_PROFILE_SELLER_MASTER_SELLER_ID",
                        column: x => x.SELLER_ID,
                        principalTable: "SELLER_MASTER",
                        principalColumn: "SELLER_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CATEGORY_TYPES",
                columns: table => new
                {
                    CATEGORY_TYPE_ID = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CATEGORY_TYPE = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CREATION_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CREATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    LAST_UPDATE_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LAST_UPDATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    UCP_ID = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CATEGORY_TYPES", x => x.CATEGORY_TYPE_ID);
                    table.ForeignKey(
                        name: "FK_CATEGORY_TYPES_USER_COMPANY_PROFILE_UCP_ID",
                        column: x => x.UCP_ID,
                        principalTable: "USER_COMPANY_PROFILE",
                        principalColumn: "UCP_ID");
                });

            migrationBuilder.CreateTable(
                name: "ENTRY",
                columns: table => new
                {
                    ENTRY_ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ENTRY_NUMBER = table.Column<long>(type: "bigint", nullable: true),
                    ENTRY_TYPE = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ENTRY_DATE = table.Column<DateTime>(type: "date", nullable: true),
                    ENTRY_REF_NO = table.Column<long>(type: "bigint", nullable: true),
                    ENTRY_REF_ID = table.Column<long>(type: "bigint", nullable: true),
                    ENTRY_REF_TYPE = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ENTRY_REF_DATE = table.Column<DateTime>(type: "date", nullable: true),
                    ENTRY_REF_CODE = table.Column<long>(type: "bigint", nullable: true),
                    ENTRY_ATTRIBUTE1 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ENTRY_ATTRIBUTE2 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ENTRY_ATTRIBUTE3 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ENTRY_ATTRIBUTE4 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ENTRY_ATTRIBUTE5 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ITEM_ID = table.Column<long>(type: "bigint", nullable: true),
                    QTY = table.Column<double>(type: "float", nullable: true),
                    LBS = table.Column<double>(type: "float", nullable: true),
                    RATE = table.Column<double>(type: "float", nullable: true),
                    AMOUNT = table.Column<double>(type: "float", nullable: true),
                    TAX_AMOUNT = table.Column<double>(type: "float", nullable: true),
                    ENTRY_ADJUST_ID = table.Column<long>(type: "bigint", nullable: true),
                    ENTRY_REMARKS = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ENTRY_COMM_ID = table.Column<long>(type: "bigint", nullable: true),
                    ENTRY_COMM_PER = table.Column<double>(type: "float", nullable: true),
                    ENTRY_COMM_AMT = table.Column<double>(type: "float", nullable: true),
                    ENTRY_TERMS_DAYS = table.Column<int>(type: "int", nullable: true),
                    ENTRY_COMM_SUKRI = table.Column<double>(type: "float", nullable: true),
                    ENTRY_POSTED_TO_FBR = table.Column<bool>(type: "bit", nullable: true),
                    ENTRY_FBR_DI_NO = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ENTRY_FBR_ERROR = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CREATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    CREATION_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LAST_UPDATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    LAST_UPDATE_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UCP_ID = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ENTRY", x => x.ENTRY_ID);
                    table.ForeignKey(
                        name: "FK_ENTRY_USER_COMPANY_PROFILE_UCP_ID",
                        column: x => x.UCP_ID,
                        principalTable: "USER_COMPANY_PROFILE",
                        principalColumn: "UCP_ID");
                });

            migrationBuilder.CreateTable(
                name: "ITEM_MASTER",
                columns: table => new
                {
                    ITEM_ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ITEM_NAME = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ITEM_ST_CODE = table.Column<double>(type: "float", nullable: true),
                    ITEM_HSCODE = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ITEM_DEACTIVE_ON = table.Column<DateTime>(type: "date", nullable: true),
                    CREATION_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CREATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    LAST_UPDATE_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LAST_UPDATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    UCP_ID = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEM_MASTER", x => x.ITEM_ID);
                    table.ForeignKey(
                        name: "FK_ITEM_MASTER_USER_COMPANY_PROFILE_UCP_ID",
                        column: x => x.UCP_ID,
                        principalTable: "USER_COMPANY_PROFILE",
                        principalColumn: "UCP_ID");
                });

            migrationBuilder.CreateTable(
                name: "MAIN_CATEGORY",
                columns: table => new
                {
                    MCATEGORY_ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MCATEGORY_NAME = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CREATION_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CREATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    LAST_UPDATE_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LAST_UPDATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    UCP_ID = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MAIN_CATEGORY", x => x.MCATEGORY_ID);
                    table.ForeignKey(
                        name: "FK_MAIN_CATEGORY_USER_COMPANY_PROFILE_UCP_ID",
                        column: x => x.UCP_ID,
                        principalTable: "USER_COMPANY_PROFILE",
                        principalColumn: "UCP_ID");
                });

            migrationBuilder.CreateTable(
                name: "TRANS",
                columns: table => new
                {
                    TRANS_ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TRANS_NO = table.Column<long>(type: "bigint", nullable: true),
                    TRANS_TYPE = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ACCOUNT_ID = table.Column<long>(type: "bigint", nullable: true),
                    DEBIT = table.Column<double>(type: "float", nullable: true),
                    CREDIT = table.Column<double>(type: "float", nullable: true),
                    REMARKS = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ENTRY_ID = table.Column<long>(type: "bigint", nullable: true),
                    UCP_ID = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANS", x => x.TRANS_ID);
                    table.ForeignKey(
                        name: "FK_TRANS_USER_COMPANY_PROFILE_UCP_ID",
                        column: x => x.UCP_ID,
                        principalTable: "USER_COMPANY_PROFILE",
                        principalColumn: "UCP_ID");
                });

            migrationBuilder.CreateTable(
                name: "USER_MASTER",
                columns: table => new
                {
                    UM_ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UM_NAME = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UM_USERNAME = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UM_CONTACT = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UM_PASSWORD = table.Column<string>(type: "nvarchar(1000)", nullable: true),
                    UM_UNIQUE_ID = table.Column<string>(type: "nvarchar(1000)", nullable: true),
                    UM_ACTIVE_ON = table.Column<DateTime>(type: "date", nullable: true),
                    UM_DEACTIVE_ON = table.Column<DateTime>(type: "date", nullable: true),
                    UM_UR_ID = table.Column<long>(type: "bigint", nullable: true),
                    CAN_CREATE = table.Column<bool>(type: "bit", nullable: false),
                    CAN_READ = table.Column<bool>(type: "bit", nullable: false),
                    CAN_UPDATE = table.Column<bool>(type: "bit", nullable: false),
                    CAN_DELETE = table.Column<bool>(type: "bit", nullable: false),
                    CREATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    CREATION_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LAST_UPDATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    LAST_UPDATE_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LAST_LOGIN = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UM_UCP_FK = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_MASTER", x => x.UM_ID);
                    table.ForeignKey(
                        name: "FK_USER_MASTER_USER_COMPANY_PROFILE_UM_UCP_FK",
                        column: x => x.UM_UCP_FK,
                        principalTable: "USER_COMPANY_PROFILE",
                        principalColumn: "UCP_ID");
                });

            migrationBuilder.CreateTable(
                name: "SUB_CATEGORY",
                columns: table => new
                {
                    SCATEGORY_ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SCATEGORY_NAME = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MCATEGORY_ID = table.Column<long>(type: "bigint", nullable: true),
                    CREATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    CREATION_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LAST_UPDATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    LAST_UPDATE_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UCP_ID = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SUB_CATEGORY", x => x.SCATEGORY_ID);
                    table.ForeignKey(
                        name: "FK_SUB_CATEGORY_MAIN_CATEGORY_MCATEGORY_ID",
                        column: x => x.MCATEGORY_ID,
                        principalTable: "MAIN_CATEGORY",
                        principalColumn: "MCATEGORY_ID");
                    table.ForeignKey(
                        name: "FK_SUB_CATEGORY_USER_COMPANY_PROFILE_UCP_ID",
                        column: x => x.UCP_ID,
                        principalTable: "USER_COMPANY_PROFILE",
                        principalColumn: "UCP_ID");
                });

            migrationBuilder.CreateTable(
                name: "USER_COMPANY_ASSIGNMENT",
                columns: table => new
                {
                    UCA_ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UM_ID = table.Column<long>(type: "bigint", nullable: false),
                    UCP_ID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_COMPANY_ASSIGNMENT", x => x.UCA_ID);
                    table.ForeignKey(
                        name: "FK_USER_COMPANY_ASSIGNMENT_USER_MASTER_UM_ID",
                        column: x => x.UM_ID,
                        principalTable: "USER_MASTER",
                        principalColumn: "UM_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CATEGORY_MASTER",
                columns: table => new
                {
                    CATEGORY_ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CATEGORY_NAME = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SCATEGORY_ID = table.Column<long>(type: "bigint", nullable: true),
                    CATEGORY_TYPE_ID = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CREATION_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CREATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    LAST_UPDATE_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LAST_UPDATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    UCP_ID = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CATEGORY_MASTER", x => x.CATEGORY_ID);
                    table.ForeignKey(
                        name: "FK_CATEGORY_MASTER_CATEGORY_TYPES_CATEGORY_TYPE_ID",
                        column: x => x.CATEGORY_TYPE_ID,
                        principalTable: "CATEGORY_TYPES",
                        principalColumn: "CATEGORY_TYPE_ID");
                    table.ForeignKey(
                        name: "FK_CATEGORY_MASTER_SUB_CATEGORY_SCATEGORY_ID",
                        column: x => x.SCATEGORY_ID,
                        principalTable: "SUB_CATEGORY",
                        principalColumn: "SCATEGORY_ID");
                    table.ForeignKey(
                        name: "FK_CATEGORY_MASTER_USER_COMPANY_PROFILE_UCP_ID",
                        column: x => x.UCP_ID,
                        principalTable: "USER_COMPANY_PROFILE",
                        principalColumn: "UCP_ID");
                });

            migrationBuilder.CreateTable(
                name: "ACCOUNT_MASTER",
                columns: table => new
                {
                    ACCOUNT_ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ACCOUNT_NAME = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CATEGORY_ID = table.Column<long>(type: "bigint", nullable: true),
                    CREATION_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CREATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    LAST_UPDATE_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LAST_UPDATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    UCP_ID = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ACCOUNT_MASTER", x => x.ACCOUNT_ID);
                    table.ForeignKey(
                        name: "FK_ACCOUNT_MASTER_CATEGORY_MASTER_CATEGORY_ID",
                        column: x => x.CATEGORY_ID,
                        principalTable: "CATEGORY_MASTER",
                        principalColumn: "CATEGORY_ID");
                    table.ForeignKey(
                        name: "FK_ACCOUNT_MASTER_USER_COMPANY_PROFILE_UCP_ID",
                        column: x => x.UCP_ID,
                        principalTable: "USER_COMPANY_PROFILE",
                        principalColumn: "UCP_ID");
                });

            migrationBuilder.CreateTable(
                name: "CUSTOMER_MASTER",
                columns: table => new
                {
                    CUSTOMER_ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CUSTOMER_NAME = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CUSTOMER_ADDRESS = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CUSTOMER_CONTACT = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CUSTOMER_EMAIL = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CUSTOMER_NTN_NO = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CUSTOMER_STR_NO = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CUSTOMER_CNIC = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CUSTOMER_DEACTIVE_ON = table.Column<DateTime>(type: "date", nullable: true),
                    ACCOUNT_ID = table.Column<long>(type: "bigint", nullable: true),
                    CREATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    CREATION_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LAST_UPDATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    LAST_UPDATE_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UCP_ID = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CUSTOMER_MASTER", x => x.CUSTOMER_ID);
                    table.ForeignKey(
                        name: "FK_CUSTOMER_MASTER_ACCOUNT_MASTER_ACCOUNT_ID",
                        column: x => x.ACCOUNT_ID,
                        principalTable: "ACCOUNT_MASTER",
                        principalColumn: "ACCOUNT_ID");
                    table.ForeignKey(
                        name: "FK_CUSTOMER_MASTER_USER_COMPANY_PROFILE_UCP_ID",
                        column: x => x.UCP_ID,
                        principalTable: "USER_COMPANY_PROFILE",
                        principalColumn: "UCP_ID");
                });

            migrationBuilder.CreateTable(
                name: "SUPPLIER_MASTER",
                columns: table => new
                {
                    SUPPLIER_ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SUPPLIER_NAME = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SUPPLIER_ADDRESS = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SUPPLIER_CONTACT = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SUPPLIER_EMAIL = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SUPPLIER_DEACTIVE_ON = table.Column<DateTime>(type: "date", nullable: true),
                    SUPPLIER_NTN_NO = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SUPPLIER_STR_NO = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SUPPLIER_CNIC = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ACCOUNT_ID = table.Column<long>(type: "bigint", nullable: true),
                    CREATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    CREATION_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LAST_UPDATED_BY = table.Column<long>(type: "bigint", nullable: true),
                    LAST_UPDATE_ON = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UCP_ID = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SUPPLIER_MASTER", x => x.SUPPLIER_ID);
                    table.ForeignKey(
                        name: "FK_SUPPLIER_MASTER_ACCOUNT_MASTER_ACCOUNT_ID",
                        column: x => x.ACCOUNT_ID,
                        principalTable: "ACCOUNT_MASTER",
                        principalColumn: "ACCOUNT_ID");
                    table.ForeignKey(
                        name: "FK_SUPPLIER_MASTER_USER_COMPANY_PROFILE_UCP_ID",
                        column: x => x.UCP_ID,
                        principalTable: "USER_COMPANY_PROFILE",
                        principalColumn: "UCP_ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ACCOUNT_MASTER_CATEGORY_ID",
                table: "ACCOUNT_MASTER",
                column: "CATEGORY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_ACCOUNT_MASTER_UCP_ID",
                table: "ACCOUNT_MASTER",
                column: "UCP_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CATEGORY_MASTER_CATEGORY_TYPE_ID",
                table: "CATEGORY_MASTER",
                column: "CATEGORY_TYPE_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CATEGORY_MASTER_SCATEGORY_ID",
                table: "CATEGORY_MASTER",
                column: "SCATEGORY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CATEGORY_MASTER_UCP_ID",
                table: "CATEGORY_MASTER",
                column: "UCP_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CATEGORY_TYPES_UCP_ID",
                table: "CATEGORY_TYPES",
                column: "UCP_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CUSTOMER_MASTER_ACCOUNT_ID",
                table: "CUSTOMER_MASTER",
                column: "ACCOUNT_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CUSTOMER_MASTER_UCP_ID",
                table: "CUSTOMER_MASTER",
                column: "UCP_ID");

            migrationBuilder.CreateIndex(
                name: "IX_ENTRY_UCP_ID",
                table: "ENTRY",
                column: "UCP_ID");

            migrationBuilder.CreateIndex(
                name: "IX_ITEM_MASTER_UCP_ID",
                table: "ITEM_MASTER",
                column: "UCP_ID");

            migrationBuilder.CreateIndex(
                name: "IX_MAIN_CATEGORY_UCP_ID",
                table: "MAIN_CATEGORY",
                column: "UCP_ID");

            migrationBuilder.CreateIndex(
                name: "IX_SUB_CATEGORY_MCATEGORY_ID",
                table: "SUB_CATEGORY",
                column: "MCATEGORY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_SUB_CATEGORY_UCP_ID",
                table: "SUB_CATEGORY",
                column: "UCP_ID");

            migrationBuilder.CreateIndex(
                name: "IX_SUPPLIER_MASTER_ACCOUNT_ID",
                table: "SUPPLIER_MASTER",
                column: "ACCOUNT_ID");

            migrationBuilder.CreateIndex(
                name: "IX_SUPPLIER_MASTER_UCP_ID",
                table: "SUPPLIER_MASTER",
                column: "UCP_ID");

            migrationBuilder.CreateIndex(
                name: "IX_TRANS_UCP_ID",
                table: "TRANS",
                column: "UCP_ID");

            migrationBuilder.CreateIndex(
                name: "IX_USER_COMPANY_ASSIGNMENT_UM_ID",
                table: "USER_COMPANY_ASSIGNMENT",
                column: "UM_ID");

            migrationBuilder.CreateIndex(
                name: "IX_USER_COMPANY_PROFILE_SELLER_ID",
                table: "USER_COMPANY_PROFILE",
                column: "SELLER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_USER_MASTER_UM_UCP_FK",
                table: "USER_MASTER",
                column: "UM_UCP_FK");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CUSTOMER_MASTER");

            migrationBuilder.DropTable(
                name: "ENTRY");

            migrationBuilder.DropTable(
                name: "ITEM_MASTER");

            migrationBuilder.DropTable(
                name: "MENU_OPTIONS");

            migrationBuilder.DropTable(
                name: "SUPPLIER_MASTER");

            migrationBuilder.DropTable(
                name: "TRANS");

            migrationBuilder.DropTable(
                name: "USER_COMPANY_ASSIGNMENT");

            migrationBuilder.DropTable(
                name: "USER_ROLE");

            migrationBuilder.DropTable(
                name: "USER_ROLE_ASSIGNMENTS");

            migrationBuilder.DropTable(
                name: "ACCOUNT_MASTER");

            migrationBuilder.DropTable(
                name: "USER_MASTER");

            migrationBuilder.DropTable(
                name: "CATEGORY_MASTER");

            migrationBuilder.DropTable(
                name: "CATEGORY_TYPES");

            migrationBuilder.DropTable(
                name: "SUB_CATEGORY");

            migrationBuilder.DropTable(
                name: "MAIN_CATEGORY");

            migrationBuilder.DropTable(
                name: "USER_COMPANY_PROFILE");

            migrationBuilder.DropTable(
                name: "SELLER_MASTER");
        }
    }
}
